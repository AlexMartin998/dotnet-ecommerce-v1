namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Procesa un mensaje entrante exactamente una vez: la marca de «ya procesado» y el efecto se
/// confirman juntos, o no se confirma ninguno de los dos.
/// </summary>
/// <remarks>
/// La marca se confirma DESPUÉS del efecto: al revés, un efecto fallido dejaba el mensaje
/// marcado y la reentrega lo descartaba como duplicado sin haberse procesado nunca. Es una
/// pieza propia, y no unas líneas del consumidor, para poder probarla sin broker.
/// </remarks>
public interface IMessageInbox
{
  /// <summary>
  /// Ejecuta <paramref name="effect"/> si este mensaje no se había procesado ya.
  /// </summary>
  /// <remarks>
  /// Quien arbitra entre réplicas es la clave primaria de <c>ProcessedMessages</c>: la que
  /// pierde el choque revienta al insertar y su efecto se deshace. El efecto debe ser
  /// replayable, porque la transacción puede reintentarse ante un fallo transitorio.
  /// </remarks>
  /// <param name="messageId">Id del mensaje, tal y como lo puso el publicador.</param>
  /// <param name="messageType">Tipo del evento, para poder auditar qué se procesó.</param>
  /// <param name="effect">Qué hacer con el mensaje. Se ejecuta dentro de la transacción.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <returns>
  /// <c>true</c> si se ejecutó ahora; <c>false</c> si ya estaba procesado y no se hizo nada.
  /// </returns>
  Task<bool> ProcessOnceAsync(
      Guid messageId, string messageType, Func<CancellationToken, Task> effect,
      CancellationToken ct = default);

  /// <summary>
  /// ¿Es este el choque de clave primaria de <c>ProcessedMessages</c>, o sea otra réplica
  /// que procesó el mismo mensaje a la vez?
  /// </summary>
  /// <remarks>
  /// Hay que mirar el número de error y no solo el tipo: un <c>catch (DbUpdateException)</c> a
  /// secas se tragaría timeouts y deadlocks, y el mensaje desaparecería sin procesarse.
  /// </remarks>
  /// <param name="exception">La excepción que salió del procesado.</param>
  bool IsConcurrentDuplicate(Exception exception);
}
