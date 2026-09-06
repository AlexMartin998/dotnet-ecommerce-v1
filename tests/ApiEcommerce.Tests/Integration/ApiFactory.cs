using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Levanta la API entera en memoria contra SQL Server y Redis <b>reales</b>, sobre una
/// base de datos propia que se recrea en cada corrida.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué no Testcontainers</b>, que es lo que decía el plan: el entorno de trabajo es
/// un dev container <b>sin Docker dentro</b>, así que no puede arrancar contenedores. Se
/// usa la infraestructura que ya está levantada en el host, con una base de datos
/// separada (<c>ApiEcommerceNET8_Tests</c>) y un prefijo propio de Redis para no pisar los
/// datos de desarrollo. Testcontainers entra en la fase 6 (CI), donde el runner sí tiene
/// Docker y es donde de verdad aporta aislamiento.
/// </para>
/// <para>
/// La base se <b>borra al empezar</b> y no al terminar: si una corrida falla, el estado
/// queda ahí para poder mirarlo. Las migraciones y el seeding los aplica el propio
/// <c>Program</c> al arrancar el host, igual que en producción — así el test también
/// cubre ese camino.
/// </para>
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
  /// <summary>
  /// Dónde vive la infraestructura de pruebas. Por variable de entorno, con el valor
  /// local por defecto.
  /// </summary>
  /// <remarks>
  /// <para>
  /// En el dev container es <c>172.17.0.1</c> (la gateway del bridge de Docker, o sea el
  /// host); en CI los <c>services</c> del runner escuchan en <c>localhost</c>. Sin esto,
  /// el mismo fichero no puede servir en los dos sitios.
  /// </para>
  /// <para>
  /// La contraseña por defecto es la del SQL Server <b>local</b> del autor, no un secreto
  /// de producción: sirve para que <c>dotnet test</c> funcione recién clonado y sin
  /// preparar nada. Cualquier entorno real —CI incluido— la pasa por
  /// <c>TEST_SQL_PASSWORD</c>.
  /// </para>
  /// </remarks>
  private static string Env(string name, string fallback)
      => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

  private static string SqlHost => Env("TEST_SQL_HOST", "172.17.0.1");
  private static string SqlPort => Env("TEST_SQL_PORT", "1434");
  private static string SqlPassword => Env("TEST_SQL_PASSWORD", "YourStrong@Passw0rd");
  private static string RedisEndpoint => Env("TEST_REDIS", "172.17.0.1:6999");

  public const string AdminUsername = "admin";
  public const string AdminPassword = "Admin123!";

  private static string ConnectionString =>
      $"Server={SqlHost},{SqlPort};Database=ApiEcommerceNET8_Tests;User ID=sa;Password={SqlPassword};" +
      "TrustServerCertificate=true;MultipleActiveResultSets=true";

  /// <summary>
  /// Ajustes que una subclase puede pisar para probar un entorno distinto (Redis caído,
  /// arranque en Production, configuración incompleta...).
  /// </summary>
  protected virtual IDictionary<string, string?> Overrides => new Dictionary<string, string?>();

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

    // Toda la configuración explícita: los tests NO deben depender de lo que haya en
    // appsettings.Development.json, que es un fichero de trabajo del autor y cambia.
    // ⚠️ UseSetting y NO ConfigureAppConfiguration. Los callbacks de
    // ConfigureAppConfiguration se aplican DESPUÉS de que Program haya ejecutado sus
    // registros, y varias piezas (AddDistributedCaching, AddMessaging, AddHealthProbes)
    // leen la configuración de forma EAGER para decidir QUÉ implementación registran.
    // Con ConfigureAppConfiguration el host arrancaba con NoIdempotencyStore y
    // NoCacheService pese a que la configuración final sí traía Redis: los tests de
    // idempotencia pasaban en verde sin probar nada. Ver TestHostGuardTests.
    var settings = Settings;

    foreach (var (key, value) in Overrides) settings[key] = value;
    foreach (var (key, value) in settings) builder.UseSetting(key, value);
  }

  private static Dictionary<string, string?> Settings =>
        new()
        {
          ["ConnectionStrings:ConexionSql"] = ConnectionString,

          ["Jwt:SecretKey"] = "clave-solo-para-tests-con-mas-de-32-caracteres-0123456789",
          ["Jwt:Issuer"] = "ApiEcommerce",
          ["Jwt:Audience"] = "ApiEcommerce.Client",
          ["Jwt:ExpirationMinutes"] = "60",

          // Redis real, con prefijo propio: comparte servidor con desarrollo pero no claves.
          ["Redis:Configuration"] = RedisEndpoint,
          ["Redis:InstanceName"] = "apiecommerce-tests:",
          ["Redis:DefaultTtlSeconds"] = "60",

          // Sin broker: publicar de verdad es la fase 5. Aquí los eventos se quedan en el
          // outbox, que es el comportamiento diseñado y no un fallo.
          ["RabbitMq:ConnectionString"] = "",

          // El admin sembrado es la única forma de tener uno: sin esto no hay manera de
          // probar la mitad de la matriz de autorización.
          ["Seed:Enabled"] = "true",
          ["Seed:AdminUsername"] = AdminUsername,
          ["Seed:AdminEmail"] = "admin@apiecommerce.test",
          ["Seed:AdminPassword"] = AdminPassword,

          // ⚠️ Sin esto la suite se limita a SÍ MISMA: el limitador particiona por IP y en
          // WebApplicationFactory todas las peticiones comparten la misma, así que a las
          // 100 peticiones toda la suite empieza a recibir 429 por un motivo que no tiene
          // nada que ver con lo que se prueba. La política se prueba aparte, bajándolos.
          ["RateLimit:GlobalPermitLimit"] = "1000000",
          ["RateLimit:AuthPermitLimit"] = "1000000",

          ["Serilog:MinimumLevel:Default"] = "Warning",
        };

  public async Task InitializeAsync()
  {
    // Antes de arrancar el host: base limpia. Program aplicará las migraciones y el
    // seeding al levantarse.
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureDeletedAsync();

    // Fuerza el arranque del host aquí y no dentro del primer test, para que un fallo de
    // migración o de seeding se lea como lo que es y no como un fallo del primer test.
    using var _ = CreateClient();
  }

  public new Task DisposeAsync() => Task.CompletedTask;

  // ---- clientes -----------------------------------------------------------

  /// <summary>Cliente sin autenticar.</summary>
  public HttpClient Anonymous() => CreateClient();

  /// <summary>Cliente autenticado como el admin sembrado.</summary>
  public Task<HttpClient> AsAdminAsync() => LoginAsync(AdminUsername, AdminPassword);

  /// <summary>
  /// Registra un usuario nuevo y devuelve su cliente autenticado. Uno nuevo por test y
  /// no uno compartido: cinco fallos de login bloquean la cuenta cinco minutos, y una
  /// cuenta compartida convierte ese bloqueo en fallos intermitentes por toda la suite.
  /// </summary>
  public async Task<HttpClient> AsNewUserAsync()
  {
    var username = $"user{Guid.NewGuid():N}"[..20];
    var client = CreateClient();

    var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
    {
      username,
      email = $"{username}@test.local",
      password = "Passw0rd!2026",
      name = "Usuario de prueba"
    });

    response.EnsureSuccessStatusCode();

    return Authenticated(client, await TokenOf(response));
  }

  private async Task<HttpClient> LoginAsync(string username, string password)
  {
    var client = CreateClient();
    var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password });

    response.EnsureSuccessStatusCode();

    return Authenticated(client, await TokenOf(response));
  }

  private static async Task<string> TokenOf(HttpResponseMessage response)
      => (await response.Content.ReadFromJsonAsync<JsonElement>())
          .GetProperty("token").GetString()!;

  private static HttpClient Authenticated(HttpClient client, string token)
  {
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return client;
  }
}
