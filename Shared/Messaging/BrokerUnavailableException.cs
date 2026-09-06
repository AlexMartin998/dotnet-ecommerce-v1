namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// El broker no está disponible. Señala <b>infraestructura caída</b>, no un mensaje malo.
/// </summary>
/// <remarks>
/// <para>
/// <b>No hereda de <c>AppException</c> a propósito</b>: nunca viaja a un cliente HTTP.
/// Una compra con el broker caído se responde 200 y el evento se queda en el outbox
/// (degradar en abierto), así que esta excepción no tiene código ni estado que mapear.
/// </para>
/// <para>
/// Existe para que <see cref="OutboxPublisher"/> pueda DISTINGUIR "la infraestructura
/// está caída" de "este mensaje concreto no se puede publicar". Cuando ambas cosas eran
/// la misma <see cref="InvalidOperationException"/>, las dos incrementaban
/// <c>Attempts</c>: medido, <b>25 segundos</b> de broker caído bastaban para enterrar un
/// evento de forma permanente — menos de lo que el propio contenedor de RabbitMQ tarda
/// en arrancar (<c>start_period: 30s</c>).
/// </para>
/// </remarks>
public sealed class BrokerUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
