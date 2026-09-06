namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Operaciones de administración sobre las colas de <b>mensajes muertos</b>: ver cuántos
/// hay parados y devolverlos a la cola principal.
/// </summary>
/// <remarks>
/// Sin esto la DLQ era un callejón sin salida: reemitir exigía la consola del broker. Se expone
/// como endpoint de administración y no como job porque reemitir es una decisión humana — un
/// mensaje que agotó sus reintentos estaba roto de verdad, y reencolarlo solo repite el fallo.
/// </remarks>
public interface IDeadLetterAdmin
{
  /// <summary>
  /// Cuántos mensajes hay parados en cada cola de dead-letters registrada.
  /// </summary>
  /// <remarks>
  /// Devuelve recuentos y no contenido: volcar los payloads de la DLQ sería una fuga, y para
  /// decidir si hay que reemitir basta con saber cuántos hay.
  /// </remarks>
  /// <returns><c>null</c> si no hay broker configurado o no responde.</returns>
  Task<IReadOnlyList<DeadLetterStatus>> GetStatusAsync(CancellationToken ct = default);

  /// <summary>
  /// Devuelve hasta <paramref name="max"/> mensajes de una cola de dead-letters a la cola
  /// principal, con el presupuesto de reintentos <b>a cero</b>.
  /// </summary>
  /// <remarks>
  /// Reiniciar el contador es la mitad del valor de la operación: con el presupuesto gastado, el
  /// mensaje moriría en la primera entrega. Publica antes de confirmar —se prefiere duplicar a
  /// perder— y el inbox deduplica, así que reemitir algo ya procesado no ejecuta nada.
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
/// Es un control de seguridad y no una validación de formulario: el nombre llega en la petición y
/// el broker es compartido con otros proyectos, así que se valida contra las suscripciones
/// registradas. 404 y no 400 porque, para quien llama, esa cola no existe.
/// </remarks>
public sealed class DeadLetterQueueNotFoundException(string queue)
    : Exceptions.NotFoundAppException("DeadLetterQueue", queue);
