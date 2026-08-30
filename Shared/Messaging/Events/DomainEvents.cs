namespace ApiEcommerce.Shared.Messaging.Events;


/// <summary>
/// Contrato de un evento de dominio publicable.
/// </summary>
/// <remarks>
/// El <see cref="EventType"/> es el nombre estable que viaja al broker y se usa como
/// routing key. Es parte del <b>contrato público</b> entre servicios: renombrar la
/// clase C# no debe romper a los consumidores, así que el nombre va explícito y no se
/// deriva de <c>typeof(T).Name</c>.
/// </remarks>
public interface IDomainEvent
{
  static abstract string EventType { get; }
}


/// <summary>
/// Se publica cuando una compra descontó stock con éxito.
/// </summary>
/// <remarks>
/// Lleva los datos que el consumidor necesita para actuar <b>sin volver a consultar</b>
/// a este servicio: un evento que obliga a llamar de vuelta al emisor reintroduce el
/// acoplamiento que la mensajería venía a quitar.
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
