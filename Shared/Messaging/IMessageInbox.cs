namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Procesa un mensaje entrante <b>exactamente una vez</b>: la marca de «ya procesado» y
/// el efecto se confirman juntos, o no se confirma ninguno de los dos.
/// </summary>
/// <remarks>
/// <para>
/// Es el gemelo de <see cref="IEventOutbox"/> —el <i>inbox pattern</i>— y el mismo
/// razonamiento que <c>ICommandLog</c> para las peticiones HTTP: una garantía de
/// «esto no se ejecuta dos veces» no puede vivir fuera de la transacción que protege.
/// </para>
/// <para>
/// ⚠️ <b>Existe porque aquí hubo un P0.</b> Antes la marca se confirmaba <i>antes</i> del
/// efecto, con el razonamiento de que así la restricción única «abría la puerta» y quien
/// perdía el choque no ejecutaba. Ese razonamiento valía cuando no había reintentos; en
/// cuanto los hubo se volvió del revés: si el efecto fallaba, la marca ya estaba
/// confirmada, y en la reentrega el mensaje se reconocía como duplicado, se hacía ack y
/// <b>desaparecía sin haberse procesado nunca</b>. Toda la maquinaria de reintentos era
/// inerte para el único caso para el que existe.
/// </para>
/// <para>
/// Y existe <b>como pieza propia</b> —en vez de seguir siendo unas líneas dentro del
/// consumidor— porque así se puede probar sin broker: un test le pasa un efecto que lanza
/// y comprueba que la marca se deshizo con él. Mientras vivió dentro de un
/// <c>BackgroundService</c> atado a AMQP, ese test no se podía escribir, y por eso el
/// arreglo del P0 se quedó sin red.
/// </para>
/// </remarks>
public interface IMessageInbox
{
  /// <summary>
  /// Ejecuta <paramref name="effect"/> si este mensaje no se había procesado ya.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Quien arbitra entre réplicas es la <b>clave primaria</b> de <c>ProcessedMessages</c>,
  /// no el código: si dos instancias procesan el mismo mensaje a la vez, una revienta al
  /// insertar y su efecto se deshace con ella.
  /// </para>
  /// <para>
  /// El efecto <b>debe ser replayable</b>: la transacción puede reintentarse ante un fallo
  /// transitorio, así que no puede dar por buena ninguna lectura del intento anterior.
  /// Es el mismo contrato que exige <c>ITransactionRunner</c>.
  /// </para>
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
  /// ⚠️ Hay que mirar el <b>número de error</b> y no conformarse con el tipo. Un
  /// <c>catch (DbUpdateException)</c> a secas se tragaría también timeouts y deadlocks
  /// (1205), haría ack, y el mensaje desaparecería de la cola <b>sin procesarse</b> y con
  /// un log que dice «duplicado ignorado». Justo la pérdida que la mensajería viene a
  /// evitar.
  /// </remarks>
  /// <param name="exception">La excepción que salió del procesado.</param>
  bool IsConcurrentDuplicate(Exception exception);
}
