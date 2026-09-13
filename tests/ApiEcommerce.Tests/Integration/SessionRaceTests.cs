using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiEcommerce.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Login sin oráculo de tiempo y lo que el front sufría (<c>planning/29</c>). Las carreras de
/// sesión están en <see cref="SessionLockTests"/>, con la ventana forzada.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class SessionRaceTests(ApiFactory factory)
{
  private const string Password = "Passw0rd!2026";
  private const string OtherPassword = "Otra0rden!2026";
  private const int Rounds = 40;

  /// <summary>Retraso del cierre en cada vuelta, barriendo de 0 a ~20 ms.</summary>
  /// <remarks>
  /// Lanzados a la vez, el logout acababa antes de que el refresh abriera su transacción y la
  /// carrera no se daba nunca (el test pasaba SIN el lock). Lo que hay que pillar es el UPDATE
  /// del cierre cayendo entre el TryConsume y el commit del refresh, y esa ventana se mueve.
  /// </remarks>
  private static TimeSpan Stagger(int round) => TimeSpan.FromMilliseconds(round % 21);

  // ---- oráculo de tiempo -----------------------------------------------------------

  [Fact]
  public async Task AnUnknownUserTakesAsLongAsAWrongPassword()
  {
    // Un intento fallido por usuario real: con 5 se bloquea, y un bloqueado responde sin hashear.
    var real = new List<string>();
    for (var i = 0; i < 8; i++) real.Add(await RegisterAsync());

    using var client = NoCookies();

    // Calentamiento: JIT y el hash ficticio, que se calcula en el primer uso.
    await client.PostAsJsonAsync("/api/v1/auth/login", new { username = "nadie-calienta", password = "x" });
    await client.PostAsJsonAsync("/api/v1/auth/login", new { username = real[0], password = "mal" });

    var known = new List<double>();
    var unknown = new List<double>();

    foreach (var username in real.Skip(1))
    {
      known.Add(await TimeLoginAsync(client, username));
      unknown.Add(await TimeLoginAsync(client, $"nadie{Guid.NewGuid():N}"[..20]));
    }

    // Holgado a propósito: sin el hash ficticio el inexistente tardaba ~1 ms frente a decenas.
    Assert.True(Median(unknown) >= Median(known) * 0.5,
        $"unknown user median {Median(unknown):0.0} ms vs wrong password {Median(known):0.0} ms");
  }

  // ---- lo que el front sufría ----------------------------------------------------------

  [Fact]
  public async Task RefreshReturnsTheSameFullUserAsLogin()
  {
    var username = await RegisterAsync();
    using var client = NoCookies();

    var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = Password });
    var loggedIn = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");

    using var refresher = WithCookie(RefreshTokenTests.CookieOf(login));
    var refreshed = (await (await refresher.PostAsync("/api/v1/auth/refresh", null))
        .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("user");

    Assert.Equal("Usuario de prueba", refreshed.GetProperty("name").GetString());
    Assert.Equal(loggedIn.GetProperty("createdAt").GetString(), refreshed.GetProperty("createdAt").GetString());
  }

  // ---- helpers -------------------------------------------------------------------------

  private static async Task<HttpResponseMessage> Later(int round, Func<Task<HttpResponseMessage>> send)
  {
    await Task.Delay(Stagger(round));
    return await send();
  }

  private HttpClient NoCookies() => factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

  private HttpClient WithCookie(string cookie)
  {
    var client = NoCookies();
    client.DefaultRequestHeaders.Add("Cookie", $"rt={cookie}");
    return client;
  }

  private async Task<string> RegisterAsync()
  {
    var username = $"race{Guid.NewGuid():N}"[..20];
    using var client = NoCookies();

    var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
    {
      username,
      email = $"{username}@test.local",
      password = Password,
      name = "Usuario de prueba"
    });
    response.EnsureSuccessStatusCode();

    return username;
  }

  private static async Task<double> TimeLoginAsync(HttpClient client, string username)
  {
    var watch = Stopwatch.StartNew();
    var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { username, password = "Contrasena1Mala" });
    watch.Stop();

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    return watch.Elapsed.TotalMilliseconds;
  }

  private static double Median(List<double> values)
  {
    var sorted = values.Order().ToList();
    return sorted[sorted.Count / 2];
  }
}


/// <summary>Host con el límite de auth en 2, para ver a quién alcanza.</summary>
public sealed class TightAuthLimitFactory : ApiFactory
{
  // Base propia: con la compartida, su arranque la BORRA a mitad de la suite.
  protected override string DatabaseName => "ApiEcommerceNET8_Tests_RateLimit";

  protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
  {
    ["RateLimit:AuthPermitLimit"] = "2",
    ["RateLimit:AuthWindowSeconds"] = "3600"
  };
}


/// <summary>El límite estricto de auth no alcanza a refresh ni a me (<c>planning/29</c>).</summary>
[Collection(IntegrationCollection.Name)]
public class AuthRateLimitTests(TightAuthLimitFactory factory) : IClassFixture<TightAuthLimitFactory>
{
  [Fact]
  public async Task TheAuthLimitStopsLoginButNotRefreshOrMe()
  {
    using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    var credentials = new { username = ApiFactory.AdminUsername, password = ApiFactory.AdminPassword };

    var first = await client.PostAsJsonAsync("/api/v1/auth/login", credentials);
    await client.PostAsJsonAsync("/api/v1/auth/login", credentials);
    var third = await client.PostAsJsonAsync("/api/v1/auth/login", credentials);

    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal((HttpStatusCode)429, third.StatusCode);

    var token = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();

    // Lo que el front llama en cada recarga sigue respondiendo con el límite de auth agotado.
    for (var i = 0; i < 3; i++)
    {
      using var me = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
      me.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
      Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(me)).StatusCode);
    }

    using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
    refresh.Headers.Add("Cookie", $"rt={RefreshTokenTests.CookieOf(first)}");
    Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(refresh)).StatusCode);
  }
}
