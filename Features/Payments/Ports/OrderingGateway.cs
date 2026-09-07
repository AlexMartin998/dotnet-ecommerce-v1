using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Repository;

namespace ApiEcommerce.Features.Payments.Ports;


/// <inheritdoc cref="IOrderingGateway"/>
public sealed class OrderingGateway(IOrderRepository orders) : IOrderingGateway
{
  public async Task<PayableOrder?> FindPayableAsync(
      int orderId, string buyerUserId, CancellationToken ct = default)
  {
    // Filtrado por comprador en la consulta: "existe pero no es tuya" ya filtra que existe.
    var order = await orders.FindForBuyerAsync(orderId, buyerUserId, ct);

    if (order is null) return null;

    // "Ya pagada" lo decide Ordering, que es de quien es la regla; Payments solo la traduce
    // a un 409.
    return new PayableOrder(
        order.Id, order.Number, order.Total, order.Currency,
        AlreadyPaid: order.Status != OrderStatus.Placed);
  }
}
