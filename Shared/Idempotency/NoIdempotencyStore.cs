namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Null Object para cuando no hay Redis configurado: la puerta siempre está abierta.
/// </summary>
/// <remarks>
/// <para>
/// Sin Redis se pierde el atajo, no la garantía. Las peticiones duplicadas llegan hasta
/// SQL y allí las arbitra la clave primaria de <c>ExecutedCommands</c>, igual que con
/// Redis caído. Que la API funcione idénticamente en un entorno sin Redis —desarrollo,
/// CI— sin renunciar a la corrección es justo lo que se ganó al bajar la garantía a la
/// transacción.
/// </para>
/// <para>
/// Antes esta clase era un problema real: al ser el almacén de idempotencia entero,
/// devolver «adelante» significaba que en cualquier entorno sin Redis <b>no había
/// idempotencia en absoluto</b>, y la única señal era su propio XML doc.
/// </para>
/// </remarks>
public sealed class NoIdempotencyStore : IIdempotencyStore
{
  // `Entered` con token vacío: no hay marcador que soltar después.
  public Task<IdempotencyGate> TryEnterAsync(string key, TimeSpan ttl, CancellationToken ct = default)
      => Task.FromResult(IdempotencyGate.Entered(string.Empty));

  public Task ReleaseAsync(string key, string fence, CancellationToken ct = default)
      => Task.CompletedTask;
}
