namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Operaciones de administración sobre las colas de <b>mensajes muertos</b>: ver cuántos
/// hay parados y devolverlos a la cola principal.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque la DLQ era un <b>callejón sin salida</b>. El consumidor sabe mandar un
/// mensaje allí cuando agota sus intentos, y desde <c>planning/20</c> el slice se entera y
/// marca la orden como fallida —así el cliente deja de esperar—, pero **reemitir el trabajo
/// exigía entrar a la consola del broker a mano**. Una cola de la que no se sale no es una
/// red de seguridad, es un vertedero.
/// </para>
/// <para>
/// ⚠️ <b>Reemitir es una decisión HUMANA, no un job.</b> Si un mensaje agotó sus reintentos
/// es porque algo estaba roto de verdad; reencolarlo solo repite el fallo, y automatizarlo
/// convierte la DLQ en un bucle caro que además esconde el incidente. Por eso esto se expone
/// como endpoint de administración y no como <c>BackgroundService</c>.
/// </para>
/// </remarks>
public interface IDeadLetterAdmin
{
  /// <summary>
  /// Cuántos mensajes hay parados en cada cola de dead-letters registrada.
  /// </summary>
  /// <remarks>
  /// ⚠️ Devuelve <b>recuentos, no contenido</b>. Un endpoint que vuelca los payloads de la
  /// DLQ es una fuga esperando a que alguien publique un evento más rico que los de hoy; y
  /// para decidir si hay que reemitir basta con saber cuántos hay.
  /// </remarks>
  /// <returns><c>null</c> si no hay broker configurado o no responde.</returns>
  Task<IReadOnlyList<DeadLetterStatus>> GetStatusAsync(CancellationToken ct = default);

  /// <summary>
  /// Devuelve hasta <paramref name="max"/> mensajes de una cola de dead-letters a la cola
  /// principal, con el presupuesto de reintentos <b>a cero</b>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>Reiniciar el contador es la mitad del valor de esta operación.</b> Sin eso el
  /// mensaje vuelve con el presupuesto gastado y muere en la primera entrega: la
  /// herramienta para recuperar mensajes no recuperaría ninguno. Es exactamente la razón
  /// por la que el contador es nuestro (<see cref="RetryAttempts"/>) y no <c>x-death</c>,
  /// que sobrevive al paso por la DLQ.
  /// </para>
  /// <para>
  /// ⚠️ <b>Publicar primero, confirmar después.</b> Al revés, morir entremedias pierde el
  /// mensaje: ya estaría confirmado en la DLQ y aún no publicado. En este orden lo peor que
  /// pasa es una reentrega, y el inbox la deduplica por <c>MessageId</c>. Misma regla que el
  /// reintento con espera: se prefiere duplicar a perder.
  /// </para>
  /// <para>
  /// Reemitir un mensaje que en realidad <i>sí</i> se procesó no ejecuta nada: lo reconoce
  /// el inbox. No hace falta llevar la cuenta de qué se ha reemitido.
  /// </para>
  /// </remarks>
  /// <param name="queue">
  /// Nombre de la cola <b>principal</b> (no el de la dead-letter). Debe ser una de las
  /// registradas; ver <see cref="DeadLetterQueueNotFoundException"/>.
  /// </param>
  /// <param name="max">Tope de mensajes a mover. La operación tiene que terminar.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <returns>Cuántos se movieron de verdad.</returns>
  /// <exception cref="DeadLetterQueueNotFoundException">La cola no está registrada.</exception>
  /// <exception cref="BrokerUnavailableException">No hay broker.</exception>
  Task<int> ReplayAsync(string queue, int max, CancellationToken ct = default);
}


/// <summary>Estado de una cola de dead-letters.</summary>
/// <param name="Queue">Cola principal a la que pertenece.</param>
/// <param name="DeadLetterQueue">Nombre real de la cola de muertos.</param>
/// <param name="Messages">Mensajes parados ahí.</param>
public readonly record struct DeadLetterStatus(string Queue, string DeadLetterQueue, uint Messages);


/// <summary>
/// Se pidió operar sobre una cola que este servicio no consume.
/// </summary>
/// <remarks>
/// ⚠️ <b>Es un control de seguridad, no una validación de formulario.</b> El nombre de la
/// cola llega en la petición, así que sin comprobarlo contra las suscripciones registradas
/// el endpoint movería mensajes de <b>cualquier</b> cola del broker — y el broker es
/// compartido con otros proyectos. La lista de suscripciones es una allowlist por
/// construcción.
/// <para>
/// **404 y no 400**: para quien llama, una cola que este servicio no consume no existe.
/// </para>
/// </remarks>
public sealed class DeadLetterQueueNotFoundException(string queue)
    : Exceptions.NotFoundAppException("DeadLetterQueue", queue);
