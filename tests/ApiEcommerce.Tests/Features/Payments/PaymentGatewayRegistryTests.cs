using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments.Gateways;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Features.Payments.Ports;
using Moq;

namespace ApiEcommerce.Tests.Features.Payments;


/// <summary>La factory del Strategy: elegir pasarela con lo que pide cada petición.</summary>
/// <remarks>
/// Es la pieza que hace que añadir PayPal sea una clase y una línea de DI, así que lo que
/// se prueba es justo eso: que resuelva por <c>Provider</c> y que los dos «no puedo» —no
/// hay ninguna, y no está esa— sean respuestas distintas.
/// </remarks>
public class PaymentGatewayRegistryTests
{
  private static IPaymentGateway AGateway(PaymentProvider provider)
  {
    var gateway = new Mock<IPaymentGateway>();

    gateway.SetupGet(g => g.Provider).Returns(provider);

    return gateway.Object;
  }

  [Fact]
  public void ForResolvesTheGatewayOfThatProvider()
  {
    var stripe = AGateway(PaymentProvider.Stripe);

    var registry = new PaymentGatewayRegistry([stripe]);

    Assert.Same(stripe, registry.For(PaymentProvider.Stripe));
    Assert.Equal([PaymentProvider.Stripe], registry.Available);
  }

  [Fact]
  public void WithoutAnyGatewayItIs503AndNot400()
  {
    // No es que el cliente pida mal: es que aquí no se puede cobrar. Cobrar no degrada en
    // abierto, así que falla ruidosamente en vez de fingir que aceptó el pago.
    var registry = new PaymentGatewayRegistry([]);

    var boom = Assert.Throws<CustomAppException>(() => registry.For(PaymentProvider.Stripe));

    Assert.Equal(HttpStatusCode.ServiceUnavailable, boom.Status);
    Assert.Equal("no_payment_provider", boom.Code);
    Assert.Empty(registry.Available);
  }

  [Fact]
  public void TwoAdaptersForTheSameProviderFailLoudly()
  {
    // Un error de registro tiene que estallar al construir el grafo, no elegir uno al azar
    // y cobrar por la pasarela equivocada.
    Assert.ThrowsAny<ArgumentException>(
        () => new PaymentGatewayRegistry([AGateway(PaymentProvider.Stripe), AGateway(PaymentProvider.Stripe)]));
  }
}
