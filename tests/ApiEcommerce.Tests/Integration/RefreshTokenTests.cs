using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Host con la ventana de gracia del reuso puesta a <b>1 segundo</b>.
/// </summary>
/// <remarks>
/// Los 15 s por defecto son lo correcto en producción —un cliente legítimo lanza varios
/// refrescos a la vez y no puede parecer un ladrón por eso— pero harían que el test de
/// detección de robo tardara 15 s. Que el plazo sea configuración es lo que permite
/// probarlo; con una constante, este test no existiría.
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
/// <para>
/// Lo que se prueba aquí no es "el endpoint responde 200", es que <b>una sesión se pueda
/// cortar</b>. Antes de esto un access token robado valía 60 minutos y no había forma de
/// invalidarlo, ni siquiera cambiando la contraseña.
/// </para>
/// <para>
/// Contra la base y Redis reales: la garantía es una fila con su índice único y la
/// denylist es una clave con TTL. Con dobles en memoria no se estaría probando ninguna
/// de las dos.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class RefreshTokenTests(ApiFactory factory)
{
  [Fact]
  public async Task LoginPutsTheRefreshTokenInAnHttpOnlyCookieAndNotInTheBody()
  {
    // El access token va en el cuerpo porque dura 15 minutos y no se renueva solo. El
    // refresh es el que hay que proteger de un XSS, y por eso viaja en una cookie que el
    // JavaScript de la página NO puede leer. Si además fuera en el cuerpo, la cookie no
    // serviría de nada.
    using var client = factory.Anonymous();

    var response = await client.PostAsJsonAsync("/api/v1/auth/login",
        new { username = "admin", password = ApiFactory.AdminPassword });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));

    // ⚠️ Sin distinguir mayúsculas: Kestrel las emite en minúscula (`httponly`), y una
    // comprobación sensible a mayúsculas da un falso negativo. Ya pasó al verificarlo.
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
    // Cerrar sesión con una cookie ya revocada, o sin cookie, tiene que ser inofensivo:
    // un error dejaría al usuario sin saber si ha salido o no.
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
    // La OPTIMIZACIÓN, no la garantía: el `jti` entra en una denylist con TTL igual a lo
    // que le quedaba de vida. Sin Redis esto no pasa y el token sobrevive hasta expirar
    // —como mucho 15 minutos— pero la sesión se corta igual, que es lo que importa.
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
    // ⚠️ El caso que hace inutilizable una detección de reuso sin ventana de gracia: el
    // cliente reusa un token recién gastado porque tenía dos peticiones en vuelo. Se
    // rechaza (ese token está gastado) pero la sesión NO se cae.
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
    // La rotación sola no detecta nada: lo que delata al ladrón es REUSAR un token ya
    // gastado, porque el legítimo ya lo cambió por otro. Y la respuesta es dura a
    // propósito: con dos copias circulando no se sabe cuál es la del dueño, así que cae
    // la sesión entera.
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
