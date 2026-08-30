using ApiEcommerce.Shared.Messaging.Events;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Encola un evento de dominio para publicarlo. La escritura ocurre en la misma
/// transacción que el cambio de negocio; la publicación al broker la hace después
/// <c>OutboxPublisher</c>.
/// </summary>
/// <remarks>
/// El servicio de negocio depende de <b>esto</b> y no de RabbitMQ. Cambiar a Kafka o
/// a Azure Service Bus no toca <c>ProductService</c>: solo la implementación de
/// <see cref="IEventPublisher"/>.
/// </remarks>
public interface IEventOutbox
{
  /// <summary>
  /// Añade el evento al outbox. <b>No hace <c>SaveChanges</c></b> a propósito: quien
  /// manda es la transacción de negocio, y el evento debe confirmarse con ella o no
  /// confirmarse en absoluto.
  /// </summary>
  Task EnqueueAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
      where TEvent : IDomainEvent;
}


/// <summary>Transporte real hacia el broker. Solo lo usa <c>OutboxPublisher</c>.</summary>
public interface IEventPublisher
{
  /// <summary>
  /// Publica un mensaje ya serializado. Debe esperar confirmación del broker
  /// (publisher confirms): sin ella, "publicado" solo significa "escrito en un socket".
  /// </summary>
  Task PublishAsync(Guid messageId, string eventType, string payload, CancellationToken ct = default);
}
