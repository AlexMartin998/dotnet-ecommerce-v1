using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Features.Payments.Ports;

namespace ApiEcommerce.Features.Payments.Gateways;


/// <inheritdoc cref="IPaymentGatewayRegistry"/>
public sealed class PaymentGatewayRegistry : IPaymentGatewayRegistry
{
  private readonly Dictionary<PaymentProvider, IPaymentGateway> _gateways;

  public PaymentGatewayRegistry(IEnumerable<IPaymentGateway> gateways)
  {
    ArgumentNullException.ThrowIfNull(gateways);

    // Por Provider y no por nombre de tipo: dos adaptadores del mismo proveedor son un
    // error de registro, y ToDictionary lo hace estallar al arrancar en vez de elegir uno.
    _gateways = gateways.ToDictionary(gateway => gateway.Provider);
  }

  public IReadOnlyCollection<PaymentProvider> Available => _gateways.Keys;

  public IPaymentGateway For(PaymentProvider provider)
  {
    // 503 y no 400: no es que el cliente pida mal, es que aquí no se puede cobrar. Cobrar no
    // es una optimización, así que esto NO degrada en abierto.
    if (_gateways.Count == 0)
      throw new CustomAppException(
          "no_payment_provider",
          "No payment provider is configured on this server.",
          HttpStatusCode.ServiceUnavailable);

    return _gateways.TryGetValue(provider, out var gateway)
        ? gateway
        : throw new CustomAppException(
            "unknown_payment_provider",
            $"Payment provider '{provider}' is not available. Available: {string.Join(", ", _gateways.Keys)}.",
            HttpStatusCode.BadRequest);
  }
}
