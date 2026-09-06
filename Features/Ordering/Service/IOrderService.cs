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
  /// <para>
  /// <b>No genera el comprobante.</b> Emite <c>OrderPlaced</c> por el outbox y devuelve; el
  /// PDF lo hace un consumidor aparte. Generarlo aquí ataría la compra a que el generador
  /// esté vivo y a que tarde poco, y ninguna de las dos cosas es cierta.
  /// </para>
  /// <para>
  /// <paramref name="intent"/> no tiene valor por defecto, igual que en
  /// <c>ProductService.BuyAsync</c>: la garantía de no comprar dos veces es una invariante
  /// de esta operación, no algo que dependa de que el controller lleve un atributo.
  /// </para>
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

  /// <summary>
  /// Abre el comprobante de una orden del comprador.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Devuelve el contenido y <b>no la clave</b>: la clave es un detalle del almacén y
  /// sacarla del servicio ataría el contrato al proveedor de hoy. Quien llama recibe un
  /// <c>Stream</c> que <b>debe liberar</b> —de eso se encarga MVC al escribir la respuesta—.
  /// </para>
  /// <para>
  /// ⚠️ La comprobación de propiedad va <b>aquí</b> y no en el controller: es una regla de
  /// negocio («un comprobante es de quien compró»), y dejarla arriba significaría que el
  /// día que lo llame un job o un endpoint nuevo se pierde en silencio. Es la misma razón
  /// por la que la idempotencia bajó al servicio en <c>planning/17</c>.
  /// </para>
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
