using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Catalog.Events;


/// <summary>
/// Se publica cuando una compra descontó stock con éxito.
/// </summary>
/// <remarks>
/// Vive en Catalog y no en <c>Shared/Messaging</c> porque habla el lenguaje del catálogo;
/// de Shared solo es el contrato <see cref="IDomainEvent"/>. Lleva todo lo que el
/// consumidor necesita para actuar sin volver a consultar a este servicio.
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
  /// <summary>Clave de enrutado del evento.</summary>
  public static string EventType => "product.purchased";
}
