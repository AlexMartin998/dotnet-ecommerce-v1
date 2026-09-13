using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Accounts.Repository;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Host con la ventana de la carrera ABIERTA a propósito y la base en READ_COMMITTED_SNAPSHOT.
/// </summary>
/// <remarks>
/// <para>
/// La sospecha de <c>planning/29</c>: entre el UPDATE que consume el refresh token y el commit que
/// inserta el siguiente, un logout podría no ver la fila nueva y la sesión sobreviviría. Lanzando
/// peticiones HTTP a la vez la ventana (milisegundos) no se acierta nunca, y un test así pasa con
/// o sin arreglo. Aquí el repositorio espera 400 ms tras consumir.
/// </para>
/// <para>
/// RCSI porque era el caso sospechoso: con versiones de fila el cierre podría leer la foto sin la
/// fila nueva. Base propia para no cambiarle el aislamiento al resto de la suite.
/// </para>
/// </remarks>
public sealed class HeldRotationFactory : ApiFactory
{
  public static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(400);

  protected override string DatabaseName => "ApiEcommerceNET8_Tests_Rcsi";

  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);

    builder.ConfigureTestServices(services =>
    {
      services.AddScoped<RefreshTokenRepository>();
      services.AddScoped<IRefreshTokenRepository>(sp => new HeldConsumeRepository(sp.GetRequiredService<RefreshTokenRepository>()));
    });
  }

  private sealed class HeldConsumeRepository(RefreshTokenRepository inner) : IRefreshTokenRepository
  {
    public async Task<bool> TryConsumeAsync(int id, CancellationToken ct = default)
    {
      var consumed = await inner.TryConsumeAsync(id, ct);
      if (consumed) await Task.Delay(Hold, ct);
      return consumed;
    }

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default) => inner.FindByHashAsync(tokenHash, ct);
    public void Add(RefreshToken token) => inner.Add(token);
    public Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct = default) => inner.RevokeFamilyAsync(familyId, ct);
    public Task<int> RevokeAllForUserAsync(string userId, CancellationToken ct = default) => inner.RevokeAllForUserAsync(userId, ct);
    public Task SaveChangesAsync(CancellationToken ct = default) => inner.SaveChangesAsync(ct);
    public Task<int> DeleteExpiredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken ct = default)
        => inner.DeleteExpiredBeforeAsync(cutoff, batchSize, ct);
  }
}


/// <summary>
/// Un refresh a mitad de rotar y un cierre de sesión a la vez: el token que se está emitiendo
/// tiene que acabar revocado (<c>planning/29</c>).
/// </summary>
/// <remarks>
/// ⚠️ Pasa SIN ningún lock: el UPDATE condicional de SQL Server espera al commit del refresh y
/// revoca también la fila nueva, con bloqueos y con RCSI. Se añadió un <c>sp_getapplock</c> por
/// usuario, este test demostró que no arreglaba nada y se retiró. Queda como guardia: si alguien
/// cambia la revocación a leer-y-escribir, falla (comprobado).
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class SessionLockTests(HeldRotationFactory factory) : IClassFixture<HeldRotationFactory>
{
  [Fact]
  public async Task ALogoutDuringARotationRevokesTheTokenBeingIssued()
  {
    await UseRowVersioningAsync();
    var (_, cookie) = await LoginAsync();

    using var refresher = WithCookie(cookie);
    using var closer = WithCookie(cookie);

    var refresh = refresher.PostAsync("/api/v1/auth/refresh", null);
    await Task.Delay(HeldRotationFactory.Hold / 3);   // dentro de la ventana
    await closer.PostAsync("/api/v1/auth/logout", null);
    await refresh;

    Assert.Equal(0, await LiveTokensInFamilyOfAsync(cookie));
  }

  [Fact]
  public async Task ALogoutEverywhereDuringARotationRevokesTheTokenBeingIssued()
  {
    await UseRowVersioningAsync();
    var (token, cookie) = await LoginAsync();

    using var refresher = WithCookie(cookie);
    using var closer = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    closer.DefaultRequestHeaders.Authorization = new("Bearer", token);

    var refresh = refresher.PostAsync("/api/v1/auth/refresh", null);
    await Task.Delay(HeldRotationFactory.Hold / 3);
    await closer.PostAsync("/api/v1/auth/logout-all", null);
    await refresh;

    Assert.Equal(0, await LiveTokensInFamilyOfAsync(cookie));
  }

  private async Task UseRowVersioningAsync()
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Idempotente; ROLLBACK IMMEDIATE porque el pool de la app tiene conexiones abiertas.
    await db.Database.ExecuteSqlRawAsync(
        "IF (SELECT is_read_committed_snapshot_on FROM sys.databases WHERE name = DB_NAME()) = 0 " +
        "ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE");
  }

  private async Task<(string Token, string Cookie)> LoginAsync()
  {
    using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    var response = await client.PostAsJsonAsync("/api/v1/auth/login",
        new { username = ApiFactory.AdminUsername, password = ApiFactory.AdminPassword });
    response.EnsureSuccessStatusCode();

    var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    return (token, RefreshTokenTests.CookieOf(response));
  }

  private HttpClient WithCookie(string cookie)
  {
    var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    client.DefaultRequestHeaders.Add("Cookie", $"rt={cookie}");
    return client;
  }

  private async Task<int> LiveTokensInFamilyOfAsync(string cookie)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Uri.UnescapeDataString(cookie))));
    var family = await db.RefreshTokens.Where(t => t.TokenHash == hash).Select(t => t.FamilyId).SingleAsync();
    var now = DateTime.Now;

    return await db.RefreshTokens.CountAsync(t => t.FamilyId == family && t.RevokedAt == null && t.ExpiresAt > now);
  }
}
