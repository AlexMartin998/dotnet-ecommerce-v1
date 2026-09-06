using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Shared.Documents;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>Casos de uso de compra.</summary>
public interface IOrderService
{
  /// <summary>
  /// Cierra una compra: aparta el stock, registra la orden y emite su evento, todo en la
  /// misma transacción.
  /// </summary>
  /// <remarks>
  /// No genera el comprobante: emite <c>OrderPlaced</c> por el outbox y un consumidor
  /// aparte dibuja el PDF. <paramref name="intent"/> no tiene valor por defecto porque no
  /// comprar dos veces es una invariante de la operación, no del controller.
  /// </remarks>
  /// <exception cref="Exceptions.ConflictAppException">Algún SKU no existe o no tiene stock.</exception>
  Task<CommandOutcome<OrderDto>> PlaceAsync(
      PlaceOrderDto dto, CommandIntent intent, string buyerUserId, string? buyerEmail,
      CancellationToken ct = default);

  /// <summary>Una orden del comprador. 404 si no es suya.</summary>
  /// <exception cref="Exceptions.NotFoundAppException">No existe, o no es de ese comprador.</exception>
  Task<OrderDto> GetForBuyerAsync(int id, string buyerUserId, CancellationToken ct = default);

  /// <summary>Las órdenes del comprador, de la más reciente a la más antigua.</summary>
  Task<PagedResult<OrderDto>> GetPagedForBuyerAsync(
      PageQuery query, string buyerUserId, CancellationToken ct = default);

  /// <summary>Abre el comprobante de una orden del comprador.</summary>
  /// <remarks>
  /// Devuelve el contenido y no la clave, que es un detalle del almacén; quien llama debe
  /// liberar el <c>Stream</c>. La comprobación de propiedad va aquí y no en el controller
  /// porque «un comprobante es de quien compró» es una regla de negocio.
  /// </remarks>
  /// <exception cref="Exceptions.NotFoundAppException">
  /// No existe, no es de ese comprador, o su documento ya no está en el almacén.
  /// </exception>
  /// <exception cref="Exceptions.ConflictAppException">
  /// Todavía se está generando (<c>receipt_not_ready</c>). Es reintentable.
  /// </exception>
  Task<DocumentContent> GetReceiptAsync(
      int orderId, string buyerUserId, CancellationToken ct = default);
}
