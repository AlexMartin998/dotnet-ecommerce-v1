namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// La cola de <b>un</b> consumidor: qué cola es, a qué eventos se suscribe y dónde van sus
/// mensajes muertos.
/// </summary>
/// <remarks>
/// La topología vivía en <see cref="RabbitMqOptions"/>, escrita para una sola cola; con un segundo
/// evento, el que no encajaba con ninguna volvía como 312 NO_ROUTE y se agotaba en el outbox sin
/// que nadie se enterara. El nombre de la cola es del slice, así que lo pasa a <c>AddEventConsumer</c>.
/// </remarks>
/// <param name="Queue">Cola que consume este consumidor.</param>
/// <param name="RoutingKey">Patrón de routing keys al que se liga la cola.</param>
/// <param name="DeadLetterExchange">
/// Exchange donde acaban los mensajes que agotaron sus reintentos. Es un campo y no una fórmula
/// porque las colas ya desplegadas llevan la DLX heredada y redeclararlas con otro valor da 406;
/// los slices nuevos usan una por cola, lo que además evita que el fanout mezcle sus muertos.
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
  /// Cola de espera, sin consumidor: su único trabajo es caducar los mensajes para que el broker
  /// los devuelva a la cola principal.
  /// </summary>
  /// <remarks>
  /// El nombre lleva el TTL dentro porque <c>x-message-ttl</c> se fija al declarar la cola:
  /// cambiar el plazo declara una cola nueva, en vez de exigir borrar la existente con sus
  /// mensajes (406). El precio es una cola huérfana y vacía por cada plazo usado.
  /// </remarks>
  public string RetryQueue(int delaySeconds) => $"{Queue}.retry.{delaySeconds}s";
}


/// <summary>
/// La suscripción <b>de un consumidor concreto</b>, para que pueda pedir la suya por DI.
/// </summary>
/// <remarks>
/// Con varias suscripciones registradas, inyectar <see cref="EventSubscription"/> a secas daría
/// «la última registrada»: un consumidor escuchando la cola de otro. Cerrado por tipo, quien se
/// equivoque no compila.
/// </remarks>
/// <typeparam name="TConsumer">El consumidor dueño de la suscripción.</typeparam>
/// <param name="Value">La suscripción.</param>
public sealed record EventSubscriptionOf<TConsumer>(EventSubscription Value);
