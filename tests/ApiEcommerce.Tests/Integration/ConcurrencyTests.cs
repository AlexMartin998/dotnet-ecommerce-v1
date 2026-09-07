using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Idempotency;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Las carreras que el proyecto tiene que soportar, con peticiones reales.</summary>
/// <remarks>
/// Siempre con <c>Task.WhenAll</c> y nunca en secuencia: en secuencia estos escenarios
/// pasan también con la implementación defectuosa.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ConcurrencyTests(ApiFactory factory)
{
  [Fact]
  public async Task FifteenSimultaneousPurchasesOverStockTenNeverOversell()
  {
    // Es un UPDATE condicional atómico y no concurrencia optimista: con RowVersion y
    // reintentos no sobrevende, pero rechaza compras válidas al agotar los reintentos.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();

    var responses = await Task.WhenAll(Enumerable.Range(0, 15)
        .Select(_ => user.PostAsJsonAsync("/api/v1/product/buy", new { sku, quantity = 1 })));

    Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
    Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

    // Lo innegociable: ni una unidad de más.
    Assert.Equal(0, await IdempotencyTests.StockOf(admin, sku));
  }

  [Fact]
  public async Task EightSimultaneousCreatesOfTheSameNameLeaveExactlyOneRow()
  {
    // Entre la comprobación de la regla y el INSERT cabe otra petición: quien garantiza
    // la unicidad es el índice único, y la regla solo da el mensaje bonito.
    using var admin = await factory.AsAdminAsync();
    var name = VersioningAndHealthTests.Unique("Race");

    var responses = await Task.WhenAll(Enumerable.Range(0, 8)
        .Select(_ => admin.PostAsJsonAsync("/api/v1/category", new { name })));

    Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
    Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

    // Ni un 500: el SqlException 2601/2627 tiene que llegar traducido.
    Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

    var listing = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>(
        "/api/v1/category?", CancellationToken.None);

    Assert.Equal(1, listing.EnumerateArray().Count(c => c.GetProperty("name").GetString() == name));
  }

  [Fact]
  public async Task SixConcurrentPurchasesWithTheSameKeyBuyOnlyOnce()
  {
    // Doble submit: el usuario pulsa seis veces antes de que responda la primera.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
    {
      var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/product/buy")
      {
        Content = JsonContent.Create(new { sku, quantity = 2 })
      };
      request.Headers.Add(IdempotentAttribute.HeaderName, key);
      return user.SendAsync(request);
    }));

    // Las que llegan en curso reciben 409 y las posteriores reproducen la respuesta con
    // 200; lo que no puede pasar es que se compre más de una vez.
    Assert.All(responses, r => Assert.True(
        r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
        $"esperaba 200 o 409, llegó {(int)r.StatusCode}"));

    Assert.Equal(8, await IdempotencyTests.StockOf(admin, sku));
  }

  [Fact]
  public async Task FourAdminsDemotingEachOtherAtOnceNeverLeaveTheSystemWithoutAdministrators()
  {
    // Contar administradores y quitar el rol son dos viajes a la base: sin serializar, los cuatro
    // cuentan cuatro, los cuatro se degradan y no queda nadie que pueda volver a repartir el rol.
    using var seed = await factory.AsAdminAsync();

    var admins = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => NewAdminAsync(seed)));

    try
    {
      // Con el admin sembrado fuera quedan exactamente cuatro, que son los que corren.
      Assert.Equal(HttpStatusCode.NoContent,
          (await Demote(admins[0].Client, await MyIdAsync(seed))).StatusCode);

      Assert.Equal(4, await AdminCountAsync());

      // En corro: cada uno degrada al siguiente, así que las cuatro bajas son legítimas por separado.
      var responses = await Task.WhenAll(admins.Select(
          (a, i) => Demote(a.Client, admins[(i + 1) % admins.Length].Id)));

      Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

      // Tres pasan y el cuarto se estrella contra la regla: para entonces ya es el último.
      Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.NoContent));
      Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

      // Lo innegociable: nunca cero.
      Assert.Equal(1, await AdminCountAsync());
    }
    finally
    {
      // La suite entera depende del admin sembrado, así que se le devuelve el rol pase lo que pase.
      await RestoreSeededAdminAsync();

      foreach (var (client, _) in admins) client.Dispose();
    }
  }

  [Fact]
  public async Task SixSimultaneousRegistrationsOfTheSameEmailLeaveExactlyOneAccount()
  {
    // `RequireUniqueEmail` no crea índice: entre la comprobación y el INSERT caben seis altas.
    var email = $"race{Guid.NewGuid():N}@test.local";

    var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RegisterAsync(email)));

    Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
    Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

    // Ni un 500: el SqlException 2601/2627 tiene que llegar traducido.
    Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

    // Y el email no queda envenenado: con dos cuentas, `FindByEmailAsync` reventaba con un 500
    // permanente y nadie podía volver a registrarlo nunca.
    var later = await RegisterAsync(email);

    Assert.Equal(HttpStatusCode.Conflict, later.StatusCode);
  }

  // ---- helpers ------------------------------------------------------------

  private const string Password = "Passw0rd!2026";

  /// <summary>Registra una cuenta con el email dado y un username nuevo.</summary>
  private async Task<HttpResponseMessage> RegisterAsync(string email)
  {
    using var anonymous = factory.Anonymous();

    return await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
    {
      username = NewUsername(),
      email,
      password = Password,
      name = "Carrera"
    });
  }

  /// <summary>Crea un usuario, lo promociona y devuelve un cliente con el rol ya en el token.</summary>
  private async Task<(HttpClient Client, string Id)> NewAdminAsync(HttpClient seed)
  {
    var username = NewUsername();

    var client = factory.Anonymous();

    var registered = await client.PostAsJsonAsync("/api/v1/auth/register", new
    {
      username,
      email = $"{username}@test.local",
      password = Password,
      name = "Carrera"
    });

    registered.EnsureSuccessStatusCode();

    var id = (await registered.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("user").GetProperty("id").GetString()!;

    (await seed.PostAsJsonAsync($"/api/v1/user/{id}/roles", new { role = Roles.Admin }))
        .EnsureSuccessStatusCode();

    // Token nuevo: el rol viaja dentro del JWT y el del registro se emitió antes de la promoción.
    var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = Password });

    login.EnsureSuccessStatusCode();

    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
        (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());

    return (client, id);
  }

  private static string NewUsername() => $"race{Guid.NewGuid():N}"[..20];

  private static Task<HttpResponseMessage> Demote(HttpClient admin, string userId)
      => admin.DeleteAsync($"/api/v1/user/{userId}/roles/{Roles.Admin}");

  private static async Task<string> MyIdAsync(HttpClient client)
      => (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetString()!;

  private async Task<int> AdminCountAsync()
  {
    using var scope = factory.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    return (await users.GetUsersInRoleAsync(Roles.Admin)).Count;
  }

  /// <summary>Devuelve el rol al admin sembrado, incluso si la carrera lo dejó sin ninguno.</summary>
  private async Task RestoreSeededAdminAsync()
  {
    using var scope = factory.Services.CreateScope();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    var seed = await users.FindByNameAsync(ApiFactory.AdminUsername);

    if (seed is not null && !await users.IsInRoleAsync(seed, Roles.Admin))
      await users.AddToRoleAsync(seed, Roles.Admin);
  }
}
