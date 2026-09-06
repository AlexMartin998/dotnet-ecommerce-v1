using ApiEcommerce.Shared.Idempotency;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// La puerta de admisión por dentro, contra Redis <b>real</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Lo que se prueba aquí es un <b>atajo</b>, no la garantía de idempotencia. Que esta
/// puerta falle no puede producir una doble ejecución: eso lo impide
/// <c>ExecutedCommands</c> desde la transacción de negocio, y está probado en
/// <c>DegradationTests</c> con Redis caído.
/// </para>
/// <para>
/// Contra Redis de verdad y no contra un doble en memoria: lo que se prueba es
/// precisamente la atomicidad de <c>SET NX</c> y del script de liberación.
/// </para>
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class IdempotencyStoreTests(ApiFactory factory)
{
  private IIdempotencyStore Store => factory.Services.GetRequiredService<IIdempotencyStore>();

  private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

  private static string NewKey() => $"gate-test:{Guid.NewGuid():N}";

  [Fact]
  public async Task TheFirstCallerEntersAndTheSecondFindsItBusy()
  {
    var key = NewKey();

    var first = await Store.TryEnterAsync(key, Ttl);
    var second = await Store.TryEnterAsync(key, Ttl);

    Assert.Equal(IdempotencyGateOutcome.Entered, first.Outcome);
    Assert.NotEmpty(first.Fence);

    Assert.Equal(IdempotencyGateOutcome.Busy, second.Outcome);
    Assert.Empty(second.Fence);
  }

  [Fact]
  public async Task ReleasingWithSomeoneElsesFenceDoesNotOpenTheGate()
  {
    // Sin comprobar el dueño, una petición cuyo marcador ya caducó borraría el marcador
    // VIVO de otra, y la puerta dejaría pasar a un tercero mientras la segunda sigue
    // ejecutando. Es el `release` canónico de un lock distribuido.
    var key = NewKey();

    var mine = await Store.TryEnterAsync(key, Ttl);

    await Store.ReleaseAsync(key, fence: "00000000000000000000000000000000");

    Assert.Equal(IdempotencyGateOutcome.Busy, (await Store.TryEnterAsync(key, Ttl)).Outcome);

    // Con el token bueno sí se suelta, que es lo que permite reintentar tras un fallo.
    await Store.ReleaseAsync(key, mine.Fence);

    Assert.Equal(IdempotencyGateOutcome.Entered, (await Store.TryEnterAsync(key, Ttl)).Outcome);
  }

  [Fact]
  public async Task AnExpiredMarkerOpensTheGateAgain()
  {
    // El marcador caduca solo si el proceso muere a mitad; si no, la clave quedaría
    // cerrada devolviendo 409 para siempre.
    //
    // ⚠️ Que caduque antes de tiempo ya NO permite una doble ejecución: la duplicada
    // pasa la puerta y va a chocar contra la clave primaria de ExecutedCommands. Cuando
    // este plazo gobernaba la GARANTÍA, era un lease sin renovación y sí abría esa
    // ventana — es la deuda que el rediseño cerró.
    var key = NewKey();

    var first = await Store.TryEnterAsync(key, TimeSpan.FromSeconds(1));
    Assert.Equal(IdempotencyGateOutcome.Entered, first.Outcome);

    await Task.Delay(TimeSpan.FromSeconds(1.5));

    var afterExpiry = await Store.TryEnterAsync(key, Ttl);

    Assert.Equal(IdempotencyGateOutcome.Entered, afterExpiry.Outcome);
    Assert.NotEqual(first.Fence, afterExpiry.Fence);
  }

  [Fact]
  public async Task OnlyOneOfManyConcurrentCallersEnters()
  {
    // Simultáneas, no en secuencia: lo que se prueba es que `SET NX` decide, y eso en
    // secuencia se cumple hasta con un read-then-write mal escrito.
    var key = NewKey();

    var gates = await Task.WhenAll(
        Enumerable.Range(0, 12).Select(_ => Store.TryEnterAsync(key, Ttl)));

    Assert.Equal(1, gates.Count(g => g.Outcome == IdempotencyGateOutcome.Entered));
    Assert.Equal(11, gates.Count(g => g.Outcome == IdempotencyGateOutcome.Busy));
  }
}
