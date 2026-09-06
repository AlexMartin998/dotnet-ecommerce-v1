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
/// Levanta la API entera en memoria contra SQL Server y Redis reales, sobre una base de
/// datos propia que se recrea en cada corrida.
/// </summary>
/// <remarks>
/// Sin Testcontainers porque el dev container no tiene Docker dentro: se usa la
/// infraestructura del host con base y prefijo de Redis separados. La base se borra al
/// empezar y no al terminar, para poder mirar el estado de una corrida que falló.
/// </remarks>
public class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
  /// <summary>
  /// Lee un ajuste de infraestructura de las variables de entorno, con el valor local por
  /// defecto para que el mismo fichero sirva en el dev container y en CI.
  /// </summary>
  /// <remarks>
  /// Los valores por defecto son los de la máquina local del autor y no secretos: están
  /// para que <c>dotnet test</c> funcione recién clonado. CI los pasa por entorno.
  /// </remarks>
  private static string Env(string name, string fallback)
      => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : fallback;

  /// <summary>Carpeta temporal de comprobantes, propia de cada corrida.</summary>
  /// <remarks>
  /// <c>Documents:RootPath</c> es relativo al content root: sin esto cada corrida dejaría
  /// PDFs dentro del repo, y dos suites en paralelo se pisarían.
  /// </remarks>
  private static readonly string DocumentsRoot =
      Path.Combine(Path.GetTempPath(), $"apiecommerce-tests-docs-{Guid.NewGuid():N}");

  private static string SqlHost => Env("TEST_SQL_HOST", "172.17.0.1");
  private static string SqlPort => Env("TEST_SQL_PORT", "1434");
  private static string SqlPassword => Env("TEST_SQL_PASSWORD", "YourStrong@Passw0rd");
  private static string RedisEndpoint => Env("TEST_REDIS", "172.17.0.1:6999");

  public const string AdminUsername = "admin";
  public const string AdminPassword = "Admin123!";

  private static string ConnectionString =>
      $"Server={SqlHost},{SqlPort};Database=ApiEcommerceNET8_Tests;User ID=sa;Password={SqlPassword};" +
      // Sin MultipleActiveResultSets: MARS desactiva los savepoints de EF Core y haría
      // que los tests corriesen en un modo transaccional distinto del real.
      "TrustServerCertificate=true";

  /// <summary>
  /// Ajustes que una subclase puede pisar para probar un entorno distinto (Redis caído,
  /// arranque en Production, configuración incompleta...).
  /// </summary>
  protected virtual IDictionary<string, string?> Overrides => new Dictionary<string, string?>();

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    builder.UseEnvironment("Testing");

    // UseSetting y no ConfigureAppConfiguration: varias piezas leen la configuración de
    // forma eager para elegir implementación, y eso pasa antes de esos callbacks.
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

          // Sin broker: los eventos se quedan en el outbox, que es el comportamiento diseñado.
          ["RabbitMq:ConnectionString"] = "",

          // El admin sembrado es la única forma de probar la mitad de la matriz de autorización.
          ["Seed:Enabled"] = "true",
          ["Seed:AdminUsername"] = AdminUsername,
          ["Seed:AdminEmail"] = "admin@apiecommerce.test",
          ["Seed:AdminPassword"] = AdminPassword,

          // Sin esto la suite se limita a sí misma: todas sus peticiones comparten IP y a
          // las 100 empiezan los 429. La política se prueba aparte, bajando el límite.
          ["RateLimit:GlobalPermitLimit"] = "1000000",
          ["RateLimit:AuthPermitLimit"] = "1000000",

          // Comprobantes fuera del repo, y proveedor explícito para probar el camino real.
          ["Documents:Provider"] = "filesystem",
          ["Documents:RootPath"] = DocumentsRoot,

          // Recolector de huérfanos apagado: un job que borra ficheros no puede depender de
          // llegar tarde. Se prueba invocándolo a mano, que además es determinista.
          ["Documents:CleanupIntervalHours"] = "0",

          ["Serilog:MinimumLevel:Default"] = "Warning",
        };

  public async Task InitializeAsync()
  {
    // Base limpia antes de arrancar: Program aplica migraciones y seeding al levantarse.
    var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(ConnectionString).Options;
    await using var db = new AppDbContext(options);
    await db.Database.EnsureDeletedAsync();

    // Arranque forzado aquí: un fallo de migración o seeding no debe leerse como un fallo
    // del primer test.
    using var _ = CreateClient();
  }

  public new Task DisposeAsync()
  {
    // La base se conserva para poder mirarla; los ficheros no son evidencia y sí basura.
    if (Directory.Exists(DocumentsRoot)) Directory.Delete(DocumentsRoot, recursive: true);

    return Task.CompletedTask;
  }

  // ---- clientes -----------------------------------------------------------

  /// <summary>Cliente sin autenticar.</summary>
  public HttpClient Anonymous() => CreateClient();

  /// <summary>Cliente autenticado como el admin sembrado.</summary>
  public Task<HttpClient> AsAdminAsync() => LoginAsync(AdminUsername, AdminPassword);

  /// <summary>Registra un usuario nuevo y devuelve su cliente autenticado.</summary>
  /// <remarks>
  /// Uno por test: cinco fallos de login bloquean la cuenta, y compartirla convertiría ese
  /// bloqueo en fallos intermitentes por toda la suite.
  /// </remarks>
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
