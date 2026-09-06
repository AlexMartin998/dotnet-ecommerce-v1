namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Null Object para cuando no hay Redis configurado: nunca reserva nada, así que el
/// filtro deja pasar todas las peticiones. Mismo patrón que <c>NoCacheService</c>.
/// </summary>
/// <remarks>
/// Elección consciente: <b>falla en abierto</b>. Sin Redis se pierde la protección
/// contra el doble submit, pero la API sigue funcionando. Si algún día la
/// idempotencia fuera un requisito duro (pagos), lo correcto sería lo contrario:
/// rechazar la petición antes que ejecutarla sin garantía.
/// </remarks>
public sealed class NoIdempotencyStore : IIdempotencyStore
{
  public Task<bool> TryAcquireAsync(
      string key, string requestHash, TimeSpan ttl, CancellationToken ct = default)
      => Task.FromResult(true);

  public Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default)
      => Task.FromResult<IdempotencyEntry?>(null);

  public Task SaveAsync(
      string key, string requestHash, IdempotentResponse response, TimeSpan ttl,
      CancellationToken ct = default)
      => Task.CompletedTask;

  public Task ReleaseAsync(string key, CancellationToken ct = default)
      => Task.CompletedTask;
}
