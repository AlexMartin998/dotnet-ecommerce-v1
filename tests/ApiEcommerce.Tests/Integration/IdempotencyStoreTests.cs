using ApiEcommerce.Shared.Idempotency;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// El almacén de idempotencia por dentro, contra Redis <b>real</b>.
/// </summary>
/// <remarks>
/// <para>
/// Estos invariantes no se pueden probar desde HTTP: piden controlar quién reserva, con
/// qué token y en qué orden. Y son justo los que fallaban en silencio — la reserva no
/// tenía dueño, así que <c>Release</c> y <c>Save</c> eran incondicionales y una petición
/// cuya reserva ya había caducado podía tirar la de otra.
/// </para>
/// <para>
/// Contra Redis de verdad y no contra un doble en memoria: lo que se está probando es
/// precisamente la atomicidad de <c>SET NX GET</c> y de los scripts Lua. Un falso en
/// memoria probaría el falso.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class IdempotencyStoreTests(ApiFactory factory)
{
  private IIdempotencyStore Store => factory.Services.GetRequiredService<IIdempotencyStore>();

  private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

  private static string NewKey() => $"store-test:{Guid.NewGuid():N}";

  private static IdempotentResponse SomeResponse(int status = 200, string body = "{\"ok\":true}")
      => new(status, body, "application/json", null);

  [Fact]
  public async Task TheFirstCallerReservesAndTheSecondSeesTheReservation()
  {
    var key = NewKey();

    var first = await Store.TryAcquireAsync(key, "hash-a", Ttl);
    var second = await Store.TryAcquireAsync(key, "hash-a", Ttl);

    Assert.Equal(IdempotencyOutcome.Acquired, first.Outcome);
    Assert.NotEmpty(first.Fence);

    // El segundo no reserva y recibe el estado que ya había, con la huella puesta
    // DESDE la reserva: sin eso, una segunda petición con otro cuerpo que llegue
    // mientras la primera sigue en curso no tendría contra qué compararse.
    Assert.Equal(IdempotencyOutcome.Existing, second.Outcome);
    Assert.Equal("hash-a", second.Entry!.RequestHash);
    Assert.Null(second.Entry.Response);
    Assert.Empty(second.Fence);
  }

  [Fact]
  public async Task ReleasingWithSomeoneElsesFenceDoesNotTouchTheReservation()
  {
    // El bug: A reserva, su acción se eterniza, la reserva caduca, B reserva y ejecuta,
    // y entonces A termina en error y hace Release — borrando la reserva VIVA de B. El
    // siguiente reintento vuelve a pasar el SET NX y ejecuta otra vez. Sin dueño, la
    // ventana de duplicación deja de estar acotada por el TTL: se reabre en cada vuelta.
    var key = NewKey();

    var mine = await Store.TryAcquireAsync(key, "hash-a", Ttl);

    await Store.ReleaseAsync(key, fence: "00000000000000000000000000000000");

    var afterForeignRelease = await Store.TryAcquireAsync(key, "hash-a", Ttl);
    Assert.Equal(IdempotencyOutcome.Existing, afterForeignRelease.Outcome);

    // Y con el token bueno sí se libera, que es lo que permite reintentar tras un fallo.
    await Store.ReleaseAsync(key, mine.Fence);

    var afterOwnRelease = await Store.TryAcquireAsync(key, "hash-a", Ttl);
    Assert.Equal(IdempotencyOutcome.Acquired, afterOwnRelease.Outcome);
  }

  [Fact]
  public async Task SavingWithSomeoneElsesFenceDoesNotOverwriteTheEntry()
  {
    // La otra mitad del mismo problema: si A termina con éxito DESPUÉS de que su reserva
    // caducara y B tomara la clave, el Save de A pisaba la entrada de B. El replay
    // devolvía entonces el cuerpo de una compra mientras en base había dos.
    var key = NewKey();

    var owner = await Store.TryAcquireAsync(key, "hash-a", Ttl);
    await Store.SaveAsync(key, owner.Fence, "hash-a", SomeResponse(body: "{\"quien\":\"dueño\"}"), Ttl);

    await Store.SaveAsync(key, "ffffffffffffffffffffffffffffffff", "hash-b",
        SomeResponse(body: "{\"quien\":\"intruso\"}"), Ttl);

    var seen = await Store.TryAcquireAsync(key, "hash-a", Ttl);

    Assert.Equal(IdempotencyOutcome.Existing, seen.Outcome);
    Assert.Equal("{\"quien\":\"dueño\"}", seen.Entry!.Response!.Body);
    Assert.Equal("hash-a", seen.Entry.RequestHash);
  }

  [Fact]
  public async Task TheSavedResponseIsWhatGetsReplayed()
  {
    var key = NewKey();

    var owner = await Store.TryAcquireAsync(key, "hash-a", Ttl);
    await Store.SaveAsync(key, owner.Fence, "hash-a", SomeResponse(201, "{\"id\":7}"), Ttl);

    var replay = await Store.TryAcquireAsync(key, "hash-a", Ttl);

    Assert.Equal(IdempotencyOutcome.Existing, replay.Outcome);
    Assert.Equal(201, replay.Entry!.Response!.StatusCode);
    Assert.Equal("{\"id\":7}", replay.Entry.Response.Body);
    Assert.Equal("application/json", replay.Entry.Response.ContentType);
  }

  [Fact]
  public async Task AnExpiredReservationLetsADuplicateThrough()
  {
    // ⚠️ Este test NO arregla nada: FIJA una limitación conocida para que nadie la
    // descubra creyendo que es un bug nuevo. La reserva es un lease SIN renovación, así
    // que una operación más lenta que el TTL libera su propia clave y una petición
    // duplicada se ejecuta de verdad. Por eso ReservationTtlSeconds es configuración:
    // debe quedar holgadamente por encima del peor caso de la acción más lenta.
    // La solución de verdad —renovar mientras la acción corre— está en planning/16 §16.6.
    var key = NewKey();

    var first = await Store.TryAcquireAsync(key, "hash-a", TimeSpan.FromSeconds(1));
    Assert.Equal(IdempotencyOutcome.Acquired, first.Outcome);

    await Task.Delay(TimeSpan.FromSeconds(1.5));

    var afterExpiry = await Store.TryAcquireAsync(key, "hash-a", Ttl);

    Assert.Equal(IdempotencyOutcome.Acquired, afterExpiry.Outcome);
    Assert.NotEqual(first.Fence, afterExpiry.Fence);
  }
}
