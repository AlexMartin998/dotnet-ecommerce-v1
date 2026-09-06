namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// La cola de <b>un</b> consumidor: qué cola es, a qué eventos se suscribe y dónde van sus
/// mensajes muertos.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque la topología vivía entera en <see cref="RabbitMqOptions"/>, escrita para
/// <b>una</b> cola y <b>un</b> consumidor. Eso bastaba con un solo evento; en cuanto hubo
/// un segundo (<c>order.placed</c>) el fallo era silencioso y feo: el publicador usa
/// <c>mandatory: true</c> con <i>publisher confirms</i>, así que un evento que no encaja
/// con ninguna cola vuelve como <b>312 NO_ROUTE</b>, el outbox lo cuenta como intento
/// fallido y el mensaje se agota en <c>MaxPublishAttempts</c>. La compra funciona y el
/// consumidor no se entera nunca.
/// </para>
/// <para>
/// <b>Quién declara qué.</b> El nombre de la cola es del <b>slice</b> —es él quien decide
/// a qué reacciona— y el mecanismo es de <c>Shared/Messaging</c>. Por eso esto es un dato
/// que el slice pasa a <c>AddEventConsumer</c>, y no una sección de configuración más:
/// añadir un consumidor no puede exigir tocar <c>Shared</c>, o la dirección
/// <b>Web → Features → Shared</b> se invierte a la primera.
/// </para>
/// </remarks>
/// <param name="Queue">Cola que consume este consumidor.</param>
/// <param name="RoutingKey">Patrón de routing keys al que se liga la cola.</param>
/// <param name="DeadLetterExchange">
/// Exchange donde acaban los mensajes que agotaron sus reintentos.
/// <para>
/// ⚠️ <b>Es un campo y no una fórmula, y esa es toda la historia.</b> La cola del catálogo
/// ya existe en los brokers con <c>x-dead-letter-exchange = apiecommerce.events.dlx</c>;
/// redeclararla con otro valor da <b>406 PRECONDITION_FAILED</b>, cierra el canal y deja
/// la mensajería abajo hasta que alguien borre la cola a mano —la trampa que ya se pisó
/// cambiando <c>RetryDelaySeconds</c>—. Así el catálogo conserva el suyo heredado y los
/// slices nuevos usan <b>uno por cola</b>.
/// </para>
/// <para>
/// ⚠️ Y una DLX por cola importa: la heredada es <c>fanout</c>, así que si dos colas
/// dead-letterearan a ella, un fallo de órdenes aparecería <b>también</b> en la DLQ del
/// catálogo. Es el mismo defecto que se midió en <c>planning/18</c> con las colas de
/// espera copiando cada reintento a todas.
/// </para>
/// </param>
public sealed record EventSubscription(string Queue, string RoutingKey, string DeadLetterExchange)
{
  /// <summary>Suscripción con una dead-letter propia, derivada del nombre de la cola.</summary>
  /// <remarks>Es la forma que usan los slices nuevos; el catálogo pasa la suya heredada.</remarks>
  public static EventSubscription For(string queue, string routingKey)
      => new(queue, routingKey, $"{queue}.dlx");

  /// <summary>Donde acaban los mensajes que agotaron sus reintentos.</summary>
  public string DeadLetterQueue => $"{Queue}.dlq";

  /// <summary>
  /// Cola de espera: no la consume nadie. Su único trabajo es <b>caducar</b> los mensajes
  /// para que el broker los devuelva a la cola principal.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>El nombre lleva el TTL dentro, y eso es lo que hace configurable
  /// <see cref="RabbitMqOptions.RetryDelaySeconds"/>.</b> El TTL vive en
  /// <c>x-message-ttl</c>, que se fija al declarar la cola: cambiarlo sobre una cola
  /// existente da <b>406 PRECONDITION_FAILED</b>, así que desplegar un plazo nuevo
  /// obligaba a borrar la cola en producción <i>con sus mensajes dentro</i>. Con el plazo
  /// en el nombre, cambiarlo declara una cola <b>nueva</b>: despliegue aditivo, sin
  /// parada, y la vieja se vacía sola porque su dead-letter sigue apuntando a la principal.
  /// </para>
  /// <para>
  /// El precio es una cola huérfana por cada plazo usado. Se ven en la UI, están vacías y
  /// se borran a mano cuando estorben — mucho más barato que una parada.
  /// </para>
  /// <para>
  /// ⚠️ Se descartó poner el TTL <b>en el mensaje</b>: en una cola FIFO, un mensaje con TTL
  /// largo bloquea a todos los de detrás aunque ya hayan caducado (head-of-line blocking),
  /// porque el broker solo mira la cabeza.
  /// </para>
  /// </remarks>
  public string RetryQueue(int delaySeconds) => $"{Queue}.retry.{delaySeconds}s";
}


/// <summary>
/// La suscripción <b>de un consumidor concreto</b>, para que pueda pedir la suya por DI.
/// </summary>
/// <remarks>
/// El envoltorio genérico no es ceremonia: con varias suscripciones registradas, inyectar
/// <see cref="EventSubscription"/> a secas daría «la última que se registró», que es un
/// bug que solo aparece al añadir el segundo consumidor y se manifiesta como un consumidor
/// escuchando la cola de otro. Con el tipo cerrado por consumidor, el que se equivoque no
/// compila.
/// </remarks>
/// <typeparam name="TConsumer">El consumidor dueño de la suscripción.</typeparam>
/// <param name="Value">La suscripción.</param>
public sealed record EventSubscriptionOf<TConsumer>(EventSubscription Value);
