using ApiEcommerce.Features.Payments.Models;

namespace ApiEcommerce.Features.Payments.Ports;


/// <summary>Elige la pasarela que pide cada petición.</summary>
/// <remarks>
/// La factory del Strategy. Se construye con todas las <see cref="IPaymentGateway"/>
/// registradas, así que <b>añadir un proveedor es una clase y un <c>AddSingleton</c></b>:
/// ni este tipo ni el servicio ni el controller se tocan.
/// </remarks>
public interface IPaymentGatewayRegistry
{
  /// <summary>Los proveedores configurados hoy. Vacío si no hay ninguno.</summary>
  IReadOnlyCollection<PaymentProvider> Available { get; }

  /// <summary>La pasarela de ese proveedor.</summary>
  /// <exception cref="Exceptions.CustomAppException">
  /// 503 <c>no_payment_provider</c> si no hay ninguna configurada; 400
  /// <c>unknown_payment_provider</c> si la pedida no está entre las disponibles.
  /// </exception>
  IPaymentGateway For(PaymentProvider provider);
}
