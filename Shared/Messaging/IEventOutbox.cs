using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Encola un evento de dominio en la misma transacción que el cambio de negocio; la
/// publicación al broker la hace después <c>OutboxPublisher</c>.
/// </summary>
/// <remarks>
/// El servicio de negocio depende de esto y no de RabbitMQ: cambiar de transporte solo toca
/// la implementación de <see cref="IEventPublisher"/>.
/// </remarks>
public interface IEventOutbox
{
  /// <summary>
  /// Añade el evento al outbox. No hace <c>SaveChanges</c>: manda la transacción de negocio,
  /// y el evento se confirma con ella o no se confirma.
  /// </summary>
  Task EnqueueAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
      where TEvent : IDomainEvent;
}


/// <summary>Transporte real hacia el broker. Solo lo usa <c>OutboxPublisher</c>.</summary>
public interface IEventPublisher
{
  /// <summary>
  /// Publica un mensaje ya serializado, esperando confirmación del broker: sin publisher
  /// confirms, "publicado" solo significa "escrito en un socket".
  /// </summary>
  Task PublishAsync(Guid messageId, string eventType, string payload, CancellationToken ct = default);
}
