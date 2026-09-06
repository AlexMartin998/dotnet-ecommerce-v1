using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Administración de cuentas: roles y bloqueo.</summary>
/// <remarks>
/// Lo que importa son las reglas duras: las que impiden que un clic deje el sistema sin
/// nadie que pueda administrarlo.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class UserAdminTests(ApiFactory factory)
{
  [Fact]
  public async Task TheListingIsPagedAndNeverLeaksCredentials()
  {
    using var admin = await factory.AsAdminAsync();

    var response = await admin.GetAsync("/api/v1/user?page=1&pageSize=5");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var body = await response.Content.ReadAsStringAsync();
    var page = JsonSerializer.Deserialize<JsonElement>(body);

    Assert.True(page.GetProperty("items").GetArrayLength() <= 5);
    Assert.True(page.GetProperty("totalItems").GetInt32() >= 1);

    // Un campo añadido al DTO "por comodidad" bastaría para publicar una credencial.
    Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("securityStamp", body, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task ANormalUserCannotAdministerAnyone()
  {
    using var user = await factory.AsNewUserAsync();

    // 403 y no 401: sabemos quién es, y no puede.
    Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/user")).StatusCode);
  }

  [Fact]
  public async Task GrantingARoleIsIdempotent()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, id) = await NewUserAsync();

    Assert.Equal(HttpStatusCode.NoContent, (await Grant(admin, id, "admin")).StatusCode);

    // Repetir no es un error: un 409 obligaría a consultar antes de cada asignación.
    Assert.Equal(HttpStatusCode.NoContent, (await Grant(admin, id, "admin")).StatusCode);

    Assert.Contains("admin", await RolesOf(admin, id));
  }

  [Fact]
  public async Task AnUnknownRoleIsRejected()
  {
    // Identity crearía el rol al vuelo, y un rol que ningún [Authorize] nombra no protege.
    using var admin = await factory.AsAdminAsync();
    var (_, id) = await NewUserAsync();

    var response = await Grant(admin, id, "superuser");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.DoesNotContain("superuser", await RolesOf(admin, id));
  }

  [Fact]
  public async Task AnAdministratorCannotRemoveTheirOwnAdminRole()
  {
    // Es el clic con el que un admin se deja fuera de su propio panel.
    using var admin = await factory.AsAdminAsync();

    var me = await MyIdAsync(admin);

    var response = await admin.DeleteAsync($"/api/v1/user/{me}/roles/admin");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

    // Y sigue siendo admin: la comprobación no se quedó a medias.
    Assert.Contains("admin", await RolesOf(admin, me));
  }

  [Fact]
  public async Task TheLastAdministratorCannotBeDemoted()
  {
    // Con el admin sembrado todavía en pie, este no es el último y la operación pasa: lo
    // que se fija es que la regla cuenta admins de verdad y no rechaza siempre.
    using var admin = await factory.AsAdminAsync();
    var (_, id) = await NewUserAsync();

    await Grant(admin, id, "admin");

    var demote = await admin.DeleteAsync($"/api/v1/user/{id}/roles/admin");

    Assert.Equal(HttpStatusCode.NoContent, demote.StatusCode);
    Assert.DoesNotContain("admin", await RolesOf(admin, id));
  }

  [Fact]
  public async Task AnAdministratorCannotLockThemselves()
  {
    // Bloquearse a uno mismo deja el panel inaccesible para su propio dueño.
    using var admin = await factory.AsAdminAsync();

    var me = await MyIdAsync(admin);

    Assert.Equal(HttpStatusCode.Conflict,
        (await admin.PostAsync($"/api/v1/user/{me}/lock", null)).StatusCode);
  }

  [Fact]
  public async Task LockingAnAccountAlsoKillsItsOpenSessions()
  {
    // Sin esto bloquear no sirve de nada: renovar no vuelve a pedir credenciales, así que
    // el usuario seguiría dentro indefinidamente.
    using var admin = await factory.AsAdminAsync();
    var (victim, id) = await NewUserAsync();

    // La sesión está abierta y puede renovarse.
    Assert.Equal(HttpStatusCode.OK, (await victim.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

    Assert.Equal(HttpStatusCode.NoContent,
        (await admin.PostAsync($"/api/v1/user/{id}/lock", null)).StatusCode);

    // Y ahora ya no.
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await victim.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

    victim.Dispose();
  }

  [Fact]
  public async Task ALockedUserCannotLogInAndCanAgainAfterUnlocking()
  {
    using var admin = await factory.AsAdminAsync();
    var (victim, id) = await NewUserAsync();
    victim.Dispose();

    await admin.PostAsync($"/api/v1/user/{id}/lock", null);

    var user = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/user/{id}");
    var username = user.GetProperty("username").GetString();

    using var anonymous = factory.Anonymous();

    var blocked = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
        new { username, password = NewUserPassword });

    // 403 y no 401: la contraseña es correcta, la cuenta está cerrada.
    Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

    Assert.Equal(HttpStatusCode.NoContent,
        (await admin.PostAsync($"/api/v1/user/{id}/unlock", null)).StatusCode);

    var allowed = await anonymous.PostAsJsonAsync("/api/v1/auth/login",
        new { username, password = NewUserPassword });

    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
  }

  // ---- helpers ------------------------------------------------------------

  private const string NewUserPassword = "Passw0rd!2026";

  /// <summary>Crea un usuario y devuelve su cliente autenticado y su id.</summary>
  private async Task<(HttpClient Client, string Id)> NewUserAsync()
  {
    var client = await factory.AsNewUserAsync();

    return (client, await MyIdAsync(client));
  }

  private static async Task<string> MyIdAsync(HttpClient client)
      => (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me")).GetProperty("id").GetString()!;

  private static Task<HttpResponseMessage> Grant(HttpClient admin, string userId, string role)
      => admin.PostAsJsonAsync($"/api/v1/user/{userId}/roles", new { role });

  private static async Task<string> RolesOf(HttpClient admin, string userId)
      => (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/user/{userId}"))
          .GetProperty("roles").ToString();
}
