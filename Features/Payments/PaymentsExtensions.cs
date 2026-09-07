using ApiEcommerce.Features.Payments.Gateways;
using ApiEcommerce.Features.Payments.Ports;
using ApiEcommerce.Features.Payments.Repository;
using ApiEcommerce.Features.Payments.Service;

namespace ApiEcommerce.Features.Payments;


/// <summary>Registro del contexto acotado Payments: cobrar una orden.</summary>
/// <remarks>
/// Toda su dependencia de las órdenes cabe en <see cref="OrderingGateway"/>, y toda su
/// dependencia de una pasarela concreta, en el <see cref="IPaymentGatewayRegistry"/>.
/// </remarks>
public static class PaymentsExtensions
{
  public static IServiceCollection AddPaymentsFeature(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<PaymentOptions>()
        .Bind(configuration.GetSection(PaymentOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddScoped<IPaymentRepository, PaymentRepository>();

    // El único punto del slice que conoce Ordering.
    services.AddScoped<IOrderingGateway, OrderingGateway>();

    services.AddScoped<IPaymentService, PaymentService>();

    AddGateways(services, configuration);

    return services;
  }

  /// <summary>
  /// Las pasarelas disponibles. **Añadir PayPal es una clase y una línea aquí**: ni el
  /// registro, ni el servicio, ni el controller se tocan.
  /// </summary>
  /// <remarks>
  /// Lectura eager de la configuración, que es el único caso en que vale: decide QUÉ
  /// implementación se registra, no cómo se comporta. Sin credenciales el proveedor no se
  /// registra y la API arranca igual; lo que falla es cobrar, con un 503 explícito, porque
  /// cobrar no es una optimización y no puede degradar en abierto.
  /// </remarks>
  private static void AddGateways(IServiceCollection services, IConfiguration configuration)
  {
    var options = configuration.GetSection(PaymentOptions.SectionName).Get<PaymentOptions>()
                  ?? new PaymentOptions();

    if (options.Stripe.IsConfigured)
      services.AddSingleton<IPaymentGateway, StripePaymentGateway>();

    // Se registra siempre, también sin pasarelas: es quien da el 503 con sentido.
    services.AddSingleton<IPaymentGatewayRegistry, PaymentGatewayRegistry>();
  }
}
