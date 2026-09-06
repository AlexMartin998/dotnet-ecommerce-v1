using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Host con la ventana de gracia del reuso puesta a 1 segundo.</summary>
/// <remarks>
/// Los 15 s por defecto son los correctos en producción, pero harían que el test de
/// detección de robo tardase 15 s. Que el plazo sea configuración es lo que lo permite.
/// </remarks>
public sealed class ShortReuseGraceFactory : ApiFactory
{
  protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
  {
    ["RefreshToken:ReuseGraceSeconds"] = "1"
  };
}


/// <summary>
/// Sesiones revocables: rotación, detección de reuso y logout.
/// </summary>
/// <remarks>
/// Lo que se prueba es que una sesión se pueda cortar. Contra la base y Redis reales: la
/// garantía es una fila con índice único y la denylist una clave con TTL, y con dobles en
/// memoria no se probaría ninguna de las dos.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class RefreshTokenTests(ApiFactory factory)
{
  [Fact]
  public async Task LoginPutsTheRefreshTokenInAnHttpOnlyCookieAndNotInTheBody()
  {
    // El refresh es el que hay que proteger de un XSS: si además viajara en el cuerpo, la
    // cookie HttpOnly no serviría de nada.
    using var client = factory.Anonymous();

    var response = await client.PostAsJsonAsync("/api/v1/auth/login",
        new { username = "admin", password = ApiFactory.AdminPassword });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

    // Sin distinguir mayúsculas: Kestrel emite `httponly` en minúscula.
    Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);

    var body = await response.Content.ReadAsStringAsync();
    Assert.DoesNotContain("refreshToken", body, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public async Task RefreshingRotatesTheTokenAndReturnsANewAccessToken()
  {
    using var client = factory.Anonymous();

    var login = await LoginAsync(client);
    var first = CookieOf(login);

    var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);

    Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
    Assert.NotEqual(first, CookieOf(refreshed));

    var body = await refreshed.Content.ReadFromJsonAsync<JsonElement>();
    Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));
  }

  [Fact]
  public async Task LogoutRevokesTheSessionSoItCannotBeRefreshedAgain()
  {
    using var client = factory.Anonymous();
    await LoginAsync(client);

    var logout = await client.PostAsync("/api/v1/auth/logout", null);
    Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

    var refreshed = await client.PostAsync("/api/v1/auth/refresh", null);
    Assert.Equal(HttpStatusCode.Unauthorized, refreshed.StatusCode);
  }

  [Fact]
  public async Task LogoutTwiceIsHarmless()
  {
    // Un error al cerrar sesión dejaría al usuario sin saber si ha salido o no.
    using var client = factory.Anonymous();
    await LoginAsync(client);

    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
    Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);

    using var virgin = factory.Anonymous();
    Assert.Equal(HttpStatusCode.NoContent, (await virgin.PostAsync("/api/v1/auth/logout", null)).StatusCode);
  }

  [Fact]
  public async Task LogoutAlsoKillsTheAccessTokenTheClientAlreadyHas()
  {
    // Es la optimización, no la garantía: sin Redis el access token sobrevive hasta
    // expirar, pero la sesión se corta igual.
    using var client = factory.Anonymous();

    var login = await LoginAsync(client);
    var access = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

    client.DefaultRequestHeaders.Authorization = new("Bearer", access);

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);

    await client.PostAsync("/api/v1/auth/logout", null);

    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
  }

  [Fact]
  public async Task TwoRefreshesInARowFromTheLegitimateClientDoNotKillTheSession()
  {
    // Un cliente legítimo con dos peticiones en vuelo reusa un token recién gastado: se
    // rechaza, pero sin ventana de gracia le tumbaría la sesión entera.
    using var client = factory.Anonymous();

    var login = await LoginAsync(client);
    var stale = CookieOf(login);

    await client.PostAsync("/api/v1/auth/refresh", null);   // rota; `stale` queda gastado

    using var replay = factory.Anonymous();
    replay.DefaultRequestHeaders.Add("Cookie", $"rt={stale}");

    Assert.Equal(HttpStatusCode.Unauthorized,
        (await replay.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

    // Y la sesión legítima sigue viva.
    Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
  }

  [Fact]
  public async Task ChangingThePasswordRevokesEveryOtherSession()
  {
    // Quien cambia la contraseña porque sospecha espera echar a los demás; si las sesiones
    // abiertas siguieran vivas, no habría echado a nadie.
    using var admin = await factory.AsNewUserAsync();

    // Otra sesión del mismo usuario, en "otro dispositivo".
    var me = await admin.GetFromJsonAsync<JsonElement>("/api/v1/auth/me");
    var username = me.GetProperty("username").GetString();

    using var otherDevice = factory.Anonymous();
    var login = await otherDevice.PostAsJsonAsync("/api/v1/auth/login",
        new { username, password = "Passw0rd!2026" });
    login.EnsureSuccessStatusCode();

    Assert.Equal(HttpStatusCode.OK,
        (await otherDevice.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

    var changed = await admin.PostAsJsonAsync("/api/v1/auth/password",
        new { currentPassword = "Passw0rd!2026", newPassword = "OtraPassw0rd!2026" });

    Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

    // El otro dispositivo se queda fuera.
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await otherDevice.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
  }

  [Fact]
  public async Task TheCurrentPasswordIsRequiredToChangeIt()
  {
    // Sin esto, un minuto frente a una sesión abierta basta para quedarse con la cuenta.
    using var user = await factory.AsNewUserAsync();

    var response = await user.PostAsJsonAsync("/api/v1/auth/password",
        new { currentPassword = "la-que-no-es", newPassword = "OtraPassw0rd!2026" });

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  // ---- helpers ------------------------------------------------------------

  internal static async Task<HttpResponseMessage> LoginAsync(HttpClient client)
  {
    var response = await client.PostAsJsonAsync("/api/v1/auth/login",
        new { username = "admin", password = ApiFactory.AdminPassword });

    response.EnsureSuccessStatusCode();

    return response;
  }

  /// <summary>Valor del refresh token que viene en la cabecera <c>Set-Cookie</c>.</summary>
  internal static string CookieOf(HttpResponseMessage response)
      => response.Headers.GetValues("Set-Cookie")
          .Select(header => header.Split(';')[0])
          .Single(pair => pair.StartsWith("rt=", StringComparison.Ordinal))["rt=".Length..];
}


/// <summary>Detección de robo: reusar un token gastado fuera de la ventana de gracia.</summary>
[Collection(IntegrationCollection.Name)]
public class RefreshTokenReuseTests(ShortReuseGraceFactory factory) : IClassFixture<ShortReuseGraceFactory>
{
  [Fact]
  public async Task ReusingASpentTokenRevokesTheWholeFamily()
  {
    // Lo que delata al ladrón es reusar un token ya gastado. La respuesta cae sobre toda
    // la familia a propósito: con dos copias circulando no se sabe cuál es la del dueño.
    using var victim = factory.Anonymous();

    var login = await RefreshTokenTests.LoginAsync(victim);
    var stolen = RefreshTokenTests.CookieOf(login);

    var rotated = await victim.PostAsync("/api/v1/auth/refresh", null);
    var legitimate = RefreshTokenTests.CookieOf(rotated);

    // Fuera de la ventana de gracia (1 s en este host): ya no es una carrera del cliente.
    await Task.Delay(TimeSpan.FromSeconds(2));

    using var thief = factory.Anonymous();
    thief.DefaultRequestHeaders.Add("Cookie", $"rt={stolen}");

    Assert.Equal(HttpStatusCode.Unauthorized,
        (await thief.PostAsync("/api/v1/auth/refresh", null)).StatusCode);

    // Y la víctima cae con él: es el precio de no saber quién es quién.
    using var afterwards = factory.Anonymous();
    afterwards.DefaultRequestHeaders.Add("Cookie", $"rt={legitimate}");

    Assert.Equal(HttpStatusCode.Unauthorized,
        (await afterwards.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
  }
}
