using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Documents;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <inheritdoc cref="IReceiptGenerator"/>
public sealed class ReceiptGenerator(
    IOrderRepository orders,
    IReceiptRenderer renderer,
    IDocumentStore documents,
    ILogger<ReceiptGenerator> logger) : IReceiptGenerator
{
  public async Task HandleAsync(OrderPlaced @event, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(@event);

    // El estado se relee de la base: el mensaje solo identifica la orden, para no tener dos
    // fuentes de verdad de lo que se imprime.
    var order = await orders.FindWithItemsAsync(@event.OrderId, ct);

    if (order is null)
    {
      // No se lanza: reintentar no va a hacer aparecer una orden borrada.
      logger.LogWarning(
          "Order {OrderId} no longer exists; skipping receipt generation", @event.OrderId);
      return;
    }

    // No es redundante con el inbox, que deduplica por MessageId: un replay desde la DLQ
    // llega con otro id y regeneraría el PDF dejando el anterior huérfano.
    if (order.ReceiptDocumentKey is not null)
    {
      logger.LogInformation(
          "Order {Number} already has a receipt; nothing to do", order.Number);
      return;
    }

    await using var content = await renderer.RenderAsync(order, ct);

    // Escribe el fichero dentro de la transacción: si el commit falla queda un huérfano,
    // que es basura recolectable, y escribir después dejaría órdenes sin su comprobante.
    var stored = await documents.SaveAsync(
        new DocumentContent(content, renderer.ContentType, $"{order.Number}.pdf"), ct);

    await orders.SetReceiptAsync(order.Id, stored.Key, ct);

    logger.LogInformation(
        "Receipt ready for order {Number} ({Size} bytes)", order.Number, stored.SizeBytes);
  }
}
