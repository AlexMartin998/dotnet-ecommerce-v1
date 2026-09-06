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

    // El estado se relee de la BASE y no se toma del mensaje. El evento trae lo justo para
    // identificar la orden a propósito (ver OrderPlaced): generar el documento a partir de
    // una copia viajada sería tener dos fuentes de verdad para lo que se imprime.
    var order = await orders.FindWithItemsAsync(@event.OrderId, ct);

    if (order is null)
    {
      // No se lanza: reintentar no la va a hacer aparecer. Solo puede pasar si alguien
      // borró la orden a mano o si se está reproduciendo un evento antiguo desde la DLQ.
      logger.LogWarning(
          "Order {OrderId} no longer exists; skipping receipt generation", @event.OrderId);
      return;
    }

    // Segunda red bajo la del inbox, y no es redundante: el inbox deduplica por MessageId,
    // así que un REPLAY manual desde la DLQ —que llega con otro id— volvería a generar el
    // PDF y dejaría el anterior huérfano en el almacén. Aquí la pregunta es sobre el
    // estado, no sobre el mensaje.
    if (order.ReceiptDocumentKey is not null)
    {
      logger.LogInformation(
          "Order {Number} already has a receipt; nothing to do", order.Number);
      return;
    }

    await using var content = await renderer.RenderAsync(order, ct);

    // ⚠️ Esto escribe un FICHERO dentro de una transacción de base de datos, y un fichero
    // no se deshace con ella: si el commit falla, queda un PDF que ninguna orden
    // referencia. Se acepta a sabiendas y en ESTA dirección — un huérfano es basura
    // recolectable (nadie lo apunta y no se alcanza sin su clave), mientras que un
    // comprobante perdido es un cliente sin su documento. La alternativa, escribir después
    // de confirmar, mueve el problema al otro lado y ahí SÍ se pierden documentos: morir
    // entremedias deja la orden diciendo que su comprobante existe.
    var stored = await documents.SaveAsync(
        new DocumentContent(content, renderer.ContentType, $"{order.Number}.pdf"), ct);

    await orders.SetReceiptAsync(order.Id, stored.Key, ct);

    logger.LogInformation(
        "Receipt ready for order {Number} ({Size} bytes)", order.Number, stored.SizeBytes);
  }
}
