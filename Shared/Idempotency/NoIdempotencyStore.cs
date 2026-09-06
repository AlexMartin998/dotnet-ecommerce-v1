namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Null Object para cuando no hay Redis configurado: la puerta siempre está abierta.
/// </summary>
/// <remarks>
/// Sin Redis se pierde el atajo, no la garantía: las duplicadas llegan a SQL y las arbitra
/// la clave primaria de <c>ExecutedCommands</c>, así que desarrollo y CI se comportan igual.
/// </remarks>
public sealed class NoIdempotencyStore : IIdempotencyStore
{
  /// <inheritdoc />
  /// <remarks><c>Entered</c> con token vacío: no hay marcador que soltar después.</remarks>
  public Task<IdempotencyGate> TryEnterAsync(string key, TimeSpan ttl, CancellationToken ct = default)
      => Task.FromResult(IdempotencyGate.Entered(string.Empty));

  /// <inheritdoc />
  public Task ReleaseAsync(string key, string fence, CancellationToken ct = default)
      => Task.CompletedTask;
}
