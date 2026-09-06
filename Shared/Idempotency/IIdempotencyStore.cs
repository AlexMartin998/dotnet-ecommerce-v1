namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Puerta de concurrencia para peticiones idénticas en vuelo. No es la garantía de
/// idempotencia: es un atajo delante de ella.
/// </summary>
/// <remarks>
/// La garantía vive en <see cref="ICommandLog"/>, dentro de la transacción de negocio. Lo
/// que aporta esto es frenar una tormenta de duplicados antes de que se apile sobre la
/// misma fila; por eso el marcador solo vive mientras la petición está en vuelo.
/// </remarks>
public interface IIdempotencyStore
{
  /// <summary>
  /// Marca la clave como «en vuelo» si nadie la tiene. Una sola operación atómica.
  /// </summary>
  /// <param name="key">Clave completa (usuario + método + ruta + clave del cliente).</param>
  /// <param name="ttl">
  /// Vida del marcador. Es solo una red por si el proceso muere a mitad: en el camino
  /// normal se libera al terminar la petición.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  Task<IdempotencyGate> TryEnterAsync(string key, TimeSpan ttl, CancellationToken ct = default);

  /// <summary>
  /// Suelta el marcador, si sigue siendo nuestro.
  /// </summary>
  /// <param name="key">Clave completa.</param>
  /// <param name="fence">
  /// Token de propiedad devuelto por <see cref="TryEnterAsync"/>. Sin él no se borra nada:
  /// un borrado incondicional dejaría que una petición ya caducada tire el marcador vivo
  /// de otra.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  Task ReleaseAsync(string key, string fence, CancellationToken ct = default);
}


/// <summary>Cómo terminó el intento de cruzar la puerta.</summary>
public enum IdempotencyGateOutcome
{
  /// <summary>Nadie más está ejecutando esta clave: adelante.</summary>
  Entered,

  /// <summary>Hay una petición idéntica en vuelo ahora mismo.</summary>
  Busy,

  /// <summary>
  /// El almacén no contestó. Se sigue adelante: la garantía no depende de esto.
  /// </summary>
  /// <remarks>
  /// Se distingue de <see cref="Entered"/> solo para poder medirlo antes de que se
  /// convierta en carga sobre SQL.
  /// </remarks>
  Unavailable
}


/// <summary>Resultado de <see cref="IIdempotencyStore.TryEnterAsync"/>.</summary>
/// <param name="Outcome">Cómo terminó el intento.</param>
/// <param name="Fence">
/// Token de propiedad del marcador que se acaba de poner, para devolvérselo a
/// <see cref="IIdempotencyStore.ReleaseAsync"/>. Vacío si no se puso ninguno.
/// </param>
public readonly record struct IdempotencyGate(IdempotencyGateOutcome Outcome, string Fence)
{
  /// <summary>Se entró en la puerta; <paramref name="fence"/> es el token de propiedad.</summary>
  public static IdempotencyGate Entered(string fence) => new(IdempotencyGateOutcome.Entered, fence);

  /// <summary>La clave ya estaba en vuelo.</summary>
  public static IdempotencyGate Busy { get; } = new(IdempotencyGateOutcome.Busy, string.Empty);

  /// <summary>El almacén no respondió.</summary>
  public static IdempotencyGate Unavailable { get; } = new(IdempotencyGateOutcome.Unavailable, string.Empty);
}
