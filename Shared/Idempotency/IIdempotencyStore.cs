namespace ApiEcommerce.Shared.Idempotency;


/// <summary>Respuesta ya emitida para una clave de idempotencia, guardada para reproducirla.</summary>
/// <param name="StatusCode">Código HTTP original.</param>
/// <param name="Body">Cuerpo serializado, o <c>null</c> si la respuesta no llevaba.</param>
/// <param name="ContentType">Tipo de contenido, o <c>null</c> (p. ej. un 204).</param>
/// <param name="Headers">
/// Cabeceras que hay que reproducir. Sin esto, el replay de un 201 perdía el
/// <c>Location</c> y el cliente que reintenta un create no podía saber qué se creó.
/// </param>
public sealed record IdempotentResponse(
    int StatusCode,
    string? Body,
    string? ContentType,
    IReadOnlyDictionary<string, string>? Headers = null);


/// <summary>
/// Estado de una clave de idempotencia: la huella del cuerpo con el que se reservó y,
/// si ya terminó, la respuesta que hay que reproducir.
/// </summary>
/// <remarks>
/// Las dos cosas viajan juntas <b>a propósito</b>: el filtro necesita comparar la huella
/// y decidir si reproduce en el mismo punto, y separarlas obligaría a dos viajes a Redis
/// por petición para responder una sola pregunta.
/// </remarks>
/// <param name="RequestHash">SHA-256 del cuerpo de la petición que reservó la clave.</param>
/// <param name="Response">La respuesta ya emitida, o <c>null</c> si sigue en curso.</param>
public sealed record IdempotencyEntry(string RequestHash, IdempotentResponse? Response);


/// <summary>
/// Almacén de claves de idempotencia. Permite que reintentar un POST no duplique el
/// efecto: la segunda petición con la misma <c>Idempotency-Key</c> devuelve la
/// respuesta de la primera en vez de volver a ejecutarla.
/// </summary>
/// <remarks>
/// <para>
/// Resuelve un problema que ni <c>RowVersion</c> ni el índice único cubren: el
/// cliente que pulsa "Comprar" dos veces, o el móvil que reintenta porque se le cayó
/// la red <b>después</b> de que el servidor procesara la compra. Ahí no hay conflicto
/// de datos que detectar — para la base son dos compras legítimamente distintas.
/// </para>
/// <para>
/// Va en Redis y no en memoria porque tiene que funcionar con varias réplicas: si
/// cada instancia tuviera su propio registro, el reintento que caiga en otra máquina
/// se ejecutaría igual.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
  /// <summary>
  /// Reserva la clave si no existe y, si ya existía, devuelve su estado. <b>Una sola
  /// operación</b>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Antes esto eran dos llamadas —un <c>GetAsync</c> para mirar y un <c>TryAcquireAsync</c>
  /// para reservar— y eso costaba dos cosas:
  /// </para>
  /// <para>
  /// 1) <b>Un viaje de más por petición.</b> Medido: 3,00 operaciones a Redis por compra
  /// con <c>Idempotency-Key</c>. Con un solo multiplexer y un timeout de 1000 ms, esa
  /// presión es la que hace que una ráfaga agote el plazo — y un acquire que expira
  /// <b>degrada en abierto</b>, o sea que la petición se ejecuta sin garantía. La
  /// garantía se apagaba sola justo cuando más falta hace.
  /// </para>
  /// <para>
  /// 2) <b>Una carrera.</b> Con dos llamadas había un camino —el que reproduce una
  /// respuesta recién terminada tras perder la reserva— que llamaba a <c>Replay</c>
  /// <b>sin comparar la huella del cuerpo</b>: si B leía antes de que A escribiera la
  /// reserva, B recibía la respuesta de A aunque su cuerpo fuera otro. Con una sola
  /// operación el estado existente <b>siempre</b> viene en la misma respuesta, así que
  /// no queda ningún camino que reproduzca sin haberlo mirado.
  /// </para>
  /// <para>
  /// Se apoya en <c>SET clave valor EX ttl NX GET</c>, que <b>exige Redis 7.0 o
  /// superior</b> (antes, combinar <c>NX</c> con <c>GET</c> era un error).
  /// </para>
  /// </remarks>
  /// <param name="key">Clave completa (usuario + método + ruta + clave del cliente).</param>
  /// <param name="requestHash">
  /// Huella del cuerpo. Se guarda <b>desde la reserva</b> y no al terminar: si sólo
  /// estuviera en la respuesta, una segunda petición con la misma clave y otro cuerpo
  /// que llegue <i>mientras la primera sigue en curso</i> no tendría contra qué comparar.
  /// </param>
  /// <param name="ttl">Vida de la RESERVA, corta: se libera sola si el proceso muere.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<IdempotencyAcquisition> TryAcquireAsync(
      string key, string requestHash, TimeSpan ttl, CancellationToken ct = default);

  /// <summary>Guarda la respuesta para poder reproducirla en los reintentos.</summary>
  /// <param name="key">Clave completa.</param>
  /// <param name="fence">
  /// Token de propiedad devuelto por <see cref="TryAcquireAsync"/>. Sólo se escribe si
  /// la reserva sigue siendo de quien llama.
  /// </param>
  /// <param name="requestHash">Huella del cuerpo que produjo esta respuesta.</param>
  /// <param name="response">La respuesta que hay que memorizar.</param>
  /// <param name="ttl">Ventana en la que se recordará.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task SaveAsync(string key, string fence, string requestHash, IdempotentResponse response, TimeSpan ttl, CancellationToken ct = default);

  /// <summary>
  /// Libera la reserva. Se llama cuando la operación <b>falló</b>: si no, un error
  /// transitorio dejaría la clave bloqueada y el cliente no podría reintentar nunca.
  /// </summary>
  /// <param name="key">Clave completa.</param>
  /// <param name="fence">
  /// Token de propiedad. <b>Sin él no se borra nada.</b> Un borrado incondicional deja
  /// que una petición cuya reserva ya caducó tire la reserva viva de otra, y entonces la
  /// ventana de duplicación deja de estar acotada por el TTL: se reabre en cada vuelta.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  Task ReleaseAsync(string key, string fence, CancellationToken ct = default);
}


/// <summary>Cómo terminó el intento de reservar una clave.</summary>
public enum IdempotencyOutcome
{
  /// <summary>La clave era nueva y queda reservada: hay que ejecutar la acción.</summary>
  Acquired,

  /// <summary>La clave ya existía; el estado que había viaja en la adquisición.</summary>
  Existing,

  /// <summary>
  /// El almacén no contestó. Se ejecuta <b>sin garantía de idempotencia</b>.
  /// </summary>
  /// <remarks>
  /// Se distingue de <see cref="Acquired"/> a propósito. Las dos ejecutan la acción
  /// —la decisión de degradar en abierto no cambia— pero sólo una de las dos protege
  /// contra el duplicado, y quien llama tiene que poder contarlo y avisar. Antes las
  /// dos devolvían el mismo <c>true</c> y la única huella era una línea de log.
  /// </remarks>
  Unavailable
}


/// <summary>Resultado de <see cref="IIdempotencyStore.TryAcquireAsync"/>.</summary>
/// <param name="Outcome">Cómo terminó el intento.</param>
/// <param name="Entry">
/// El estado que ya había, sólo cuando <paramref name="Outcome"/> es
/// <see cref="IdempotencyOutcome.Existing"/>.
/// </param>
/// <param name="Fence">
/// Token de propiedad de la reserva que se acaba de tomar, para devolvérselo luego a
/// <see cref="IIdempotencyStore.SaveAsync"/> o <see cref="IIdempotencyStore.ReleaseAsync"/>.
/// Vacío cuando no se reservó nada.
/// </param>
public readonly record struct IdempotencyAcquisition(
    IdempotencyOutcome Outcome, IdempotencyEntry? Entry, string Fence)
{
  public static IdempotencyAcquisition Acquired(string fence)
      => new(IdempotencyOutcome.Acquired, null, fence);

  public static IdempotencyAcquisition Unavailable { get; } =
      new(IdempotencyOutcome.Unavailable, null, string.Empty);

  public static IdempotencyAcquisition Existing(IdempotencyEntry entry)
      => new(IdempotencyOutcome.Existing, entry, string.Empty);
}
