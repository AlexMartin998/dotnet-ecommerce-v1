namespace ApiEcommerce.Features.Payments.Ports;


/// <summary>Lo que Payments necesita saber de una orden para cobrarla.</summary>
/// <remarks>
/// El importe se copia de aquí y se congela en el pago: lo que se cobró no puede cambiar
/// porque la orden cambie después.
/// </remarks>
public readonly record struct PayableOrder(
    int Id, string Number, decimal Total, string Currency, bool AlreadyPaid);


/// <summary>Lo único de Payments que conoce el contexto de órdenes.</summary>
/// <remarks>
/// Gemelo de <c>ICatalogGateway</c>: toda la dependencia cruzada cabe en una clase
/// adaptadora, que es lo que hace que un slice se pueda mover.
/// </remarks>
public interface IOrderingGateway
{
  /// <summary>La orden de ese comprador, o <c>null</c> si no existe o no es suya.</summary>
  Task<PayableOrder?> FindPayableAsync(
      int orderId, string buyerUserId, CancellationToken ct = default);
}
