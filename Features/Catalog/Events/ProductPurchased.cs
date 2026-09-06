using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Catalog.Events;


/// <summary>
/// Se publica cuando una compra descontó stock con éxito.
/// </summary>
/// <remarks>
/// <para>
/// Vive en <b>Catalog</b> y no en <c>Shared/Messaging</c> porque es vocabulario del
/// catálogo: habla de SKU, de stock y de producto. Lo que sí es de todos es el contrato
/// <see cref="IDomainEvent"/> — el mecanismo—, y ese sí vive en <c>Shared</c>.
/// La regla es la de siempre: <i>¿esto tiene lenguaje propio de un contexto, o es
/// mecanismo de ninguno?</i>
/// </para>
/// <para>
/// Lleva los datos que el consumidor necesita para actuar <b>sin volver a consultar</b>
/// a este servicio: un evento que obliga a llamar de vuelta al emisor reintroduce el
/// acoplamiento que la mensajería venía a quitar.
/// </para>
/// </remarks>
public sealed record ProductPurchased(
    int ProductId,
    string Sku,
    string ProductName,
    int Quantity,
    int RemainingStock,
    decimal UnitPrice,
    string? BuyerUserId,
    DateTime OccurredAt) : IDomainEvent
{
  public static string EventType => "product.purchased";
}
