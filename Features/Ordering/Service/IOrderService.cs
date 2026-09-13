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
  Task<OrderDto> GetForBuyerAsync(Guid publicId, string buyerUserId, CancellationToken ct = default);

  /// <summary>Las órdenes del comprador, de la más reciente a la más antigua.</summary>
  Task<PagedResult<OrderDto>> GetPagedForBuyerAsync(
      PageQuery query, string buyerUserId, CancellationToken ct = default);

  /// <summary>Las órdenes de todos los compradores, opcionalmente filtradas por número.</summary>
  /// <remarks>
  /// Es la vista de administración: devuelve datos de compra de terceros, así que quien la
  /// llame tiene que exigir el rol antes. El filtro es por prefijo del número.
  /// </remarks>
  Task<PagedResult<OrderDto>> GetPagedForAdminAsync(
      PageQuery query, string? number, CancellationToken ct = default);

  /// <summary>Abre el comprobante de una orden del comprador.</summary>
  /// <remarks>
  /// Devuelve el contenido y no la clave, que es un detalle del almacén; quien llama debe
  /// liberar el <c>Stream</c>. La comprobación de propiedad va aquí y no en el controller
  /// porque «un comprobante es de quien compró» es una regla de negocio.
  /// </remarks>
  /// <exception cref="Exceptions.NotFoundAppException">
  /// No existe, no es de ese comprador, o su documento ya no está en el almacén.
  /// </exception>
  /// <exception cref="Exceptions.CustomAppException">
  /// 409 con <c>receipt_not_ready</c> si todavía se genera (reintentable) o con
  /// <c>receipt_failed</c> si su generación falló (definitivo).
  /// </exception>
  Task<DocumentContent> GetReceiptAsync(
      Guid publicId, string buyerUserId, CancellationToken ct = default);

  /// <summary>Mueve una orden por su ciclo de vida de entrega. Solo para administradores.</summary>
  /// <remarks>
  /// No hace falta <c>Idempotency-Key</c>: la transición es un UPDATE condicional, así que
  /// reenviarla no repite nada. Tampoco emite ningún evento — nadie consume
  /// <c>order.shipped</c> y publicar sin cola que lo acepte agota el outbox en silencio.
  /// </remarks>
  /// <exception cref="Exceptions.BadOperationAppException">El destino no es alcanzable.</exception>
  /// <exception cref="Exceptions.NotFoundAppException">No hay ninguna orden con ese publicId.</exception>
  /// <exception cref="Exceptions.ConflictAppException">La orden no venía del estado que ese destino exige.</exception>
  Task AdvanceAsync(Guid publicId, UpdateOrderStatusDto dto, CancellationToken ct = default);

  /// <summary>Contadores de órdenes por estado, para el panel. Solo administración.</summary>
  Task<OrderStatsDto> GetStatsAsync(CancellationToken ct = default);
}
