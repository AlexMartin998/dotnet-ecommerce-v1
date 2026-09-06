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
  // `Acquired` y no `Unavailable`: aquí no hay ninguna anomalía que contar. Que no haya
  // Redis configurado es un hecho del arranque, no un fallo en caliente — avisar en cada
  // petición de algo que ya se sabe desde que la app levantó sería ruido. La DECISIÓN,
  // que es lo que exige la regla §8, sí es la misma en las dos implementaciones:
  // ejecutar la acción.
  public Task<IdempotencyAcquisition> TryAcquireAsync(
      string key, string requestHash, TimeSpan ttl, CancellationToken ct = default)
      => Task.FromResult(IdempotencyAcquisition.Acquired(string.Empty));

  public Task SaveAsync(
      string key, string fence, string requestHash, IdempotentResponse response, TimeSpan ttl,
      CancellationToken ct = default)
      => Task.CompletedTask;

  public Task ReleaseAsync(string key, string fence, CancellationToken ct = default)
      => Task.CompletedTask;
}
