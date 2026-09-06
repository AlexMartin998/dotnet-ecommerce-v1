namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// El broker no está disponible. Señala <b>infraestructura caída</b>, no un mensaje malo.
/// </summary>
/// <remarks>
/// No hereda de <c>AppException</c>: nunca viaja a un cliente HTTP. Existe para que
/// <see cref="OutboxPublisher"/> distinga "la infraestructura está caída" de "este mensaje
/// no se puede publicar", porque solo lo segundo debe consumir intentos.
/// </remarks>
public sealed class BrokerUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
