using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Models;
using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>Escribe el evento como una fila más del <c>AppDbContext</c>.</summary>
public sealed class EventOutbox(AppDbContext db) : IEventOutbox
{
  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  public Task EnqueueAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
      where TEvent : IDomainEvent
  {
    ArgumentNullException.ThrowIfNull(domainEvent);

    db.OutboxMessages.Add(new OutboxMessage
    {
      Type = TEvent.EventType,
      Payload = JsonSerializer.Serialize(domainEvent, SerializerOptions)
    });

    // Sin SaveChangesAsync: lo confirma la transacción de negocio (ver IEventOutbox).
    return Task.CompletedTask;
  }
}
