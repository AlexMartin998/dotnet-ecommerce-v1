using ApiEcommerce.Features.Payments.Models;

namespace ApiEcommerce.Features.Payments.Ports;


/// <summary>Lo que hay que pedirle a la pasarela para crear un cobro.</summary>
public readonly record struct PaymentRequest(
    string Reference, decimal Amount, string Currency, string OrderNumber, string? BuyerEmail);


/// <summary>Lo que devuelve la pasarela al crear el intento.</summary>
/// <remarks>
/// <paramref name="ClientSecret"/> es lo único que necesita el front para confirmar el pago
/// sin que el importe pase por nuestras manos otra vez.
/// </remarks>
public readonly record struct GatewayIntent(string ProviderPaymentId, string? ClientSecret);


/// <summary>Un evento de la pasarela, ya verificado y traducido a nuestro vocabulario.</summary>
public readonly record struct GatewayEvent(
    string EventId, string Type, string ProviderPaymentId, PaymentStatus Status, string? FailureReason);


/// <summary>
/// Una pasarela de pago concreta. Hay una implementación por proveedor y la elige
/// <see cref="IPaymentGatewayRegistry"/> con lo que pide cada petición.
/// </summary>
/// <remarks>
/// ⚠️ Esto es <b>Strategy</b>, y es la excepción consciente a la regla del repo de que la
/// implementación de un puerto se elige una vez en el composition root: allí quien elige es
/// la infraestructura (disco o S3), aquí quien elige es <b>el comprador</b>, en cada
/// petición. Cuando el que decide es el request, la decisión no puede vivir en el grafo de DI.
/// </remarks>
public interface IPaymentGateway
{
  /// <summary>A qué proveedor responde esta implementación.</summary>
  PaymentProvider Provider { get; }

  /// <summary>Crea el intento de cobro y devuelve lo que el cliente necesita para confirmarlo.</summary>
  Task<GatewayIntent> CreateIntentAsync(PaymentRequest request, CancellationToken ct = default);

  /// <summary>
  /// Verifica la firma de un webhook y lo traduce, o <c>null</c> si el evento no nos interesa.
  /// </summary>
  /// <remarks>
  /// Recibe el cuerpo <b>crudo</b>: reserializar el JSON cambia bytes y el HMAC deja de
  /// cuadrar. La verificación incluye la marca de tiempo, o una firma capturada valdría para
  /// siempre.
  /// </remarks>
  /// <exception cref="Exceptions.BadOperationAppException">La firma no es válida o está caducada.</exception>
  GatewayEvent? ParseEvent(string rawPayload, string? signatureHeader);
}
