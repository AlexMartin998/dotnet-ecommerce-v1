using ApiEcommerce.Shared.Idempotency;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>La puerta de admisión por dentro, contra Redis real.</summary>
/// <remarks>
/// Es un atajo y no la garantía: que falle no produce doble ejecución, eso lo impide
/// <c>ExecutedCommands</c>. Contra Redis de verdad porque lo que se prueba es la
/// atomicidad de <c>SET NX</c> y del script de liberación.
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
    // Sin comprobar el dueño, una petición con el marcador caducado borraría el marcador
    // vivo de otra y la puerta dejaría entrar a un tercero.
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
    // Sin caducidad, un proceso que muere a mitad dejaría la clave cerrada en 409 para
    // siempre. Caducar antes de tiempo no abre una doble ejecución: la duplicada choca
    // contra la clave primaria de ExecutedCommands.
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
    // Simultáneas: en secuencia esto se cumple hasta con un read-then-write mal escrito.
    var key = NewKey();

    var gates = await Task.WhenAll(
        Enumerable.Range(0, 12).Select(_ => Store.TryEnterAsync(key, Ttl)));

    Assert.Equal(1, gates.Count(g => g.Outcome == IdempotencyGateOutcome.Entered));
    Assert.Equal(11, gates.Count(g => g.Outcome == IdempotencyGateOutcome.Busy));
  }
}
