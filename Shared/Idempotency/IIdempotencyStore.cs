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
  /// Reserva la clave de forma atómica. <c>true</c> = eres el primero y debes ejecutar.
  /// </summary>
  /// <remarks>
  /// Es un <c>SET key value NX EX ttl</c>: comprobar y reservar en <b>una</b> operación.
  /// Un <c>GET</c> seguido de un <c>SET</c> volvería a ser read-then-write y dos
  /// peticiones simultáneas con la misma clave pasarían las dos.
  /// </remarks>
  /// <param name="requestHash">
  /// Huella del cuerpo. Se guarda <b>desde la reserva</b> y no al terminar: si solo
  /// estuviera en la respuesta, una segunda petición con la misma clave y otro cuerpo
  /// que llegue <i>mientras la primera sigue en curso</i> no tendría contra qué comparar.
  /// </param>
  Task<bool> TryAcquireAsync(string key, string requestHash, TimeSpan ttl, CancellationToken ct = default);

  /// <summary>Estado de la clave, o <c>null</c> si no existe.</summary>
  Task<IdempotencyEntry?> GetAsync(string key, CancellationToken ct = default);

  /// <summary>Guarda la respuesta para poder reproducirla en los reintentos.</summary>
  Task SaveAsync(string key, string requestHash, IdempotentResponse response, TimeSpan ttl, CancellationToken ct = default);

  /// <summary>
  /// Libera la reserva. Se llama cuando la operación <b>falló</b>: si no, un error
  /// transitorio dejaría la clave bloqueada y el cliente no podría reintentar nunca.
  /// </summary>
  Task ReleaseAsync(string key, CancellationToken ct = default);
}
