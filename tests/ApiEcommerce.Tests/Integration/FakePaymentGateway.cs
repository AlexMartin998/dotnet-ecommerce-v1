using System.Text.Json;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Features.Payments.Ports;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Una pasarela de mentira, para recorrer el flujo entero sin salir a Internet.</summary>
/// <remarks>
/// Sustituye a la de Stripe en el contenedor de los tests de pagos. Que baste con cambiar
/// esta pieza es exactamente lo que el Strategy promete: ni el servicio ni el controller
/// saben qué pasarela hay detrás. La verificación de firma real se prueba aparte, contra
/// <c>StripePaymentGateway</c>.
/// </remarks>
public sealed class FakePaymentGateway : IPaymentGateway
{
  /// <summary>La «firma» que esta pasarela acepta.</summary>
  public const string ValidSignature = "firma-buena";

  public PaymentProvider Provider => PaymentProvider.Stripe;

  /// <summary>Cuántos intentos se han creado. Sirve para comprobar que no se duplican.</summary>
  public int CreatedIntents { get; private set; }

  public Task<GatewayIntent> CreateIntentAsync(
      PaymentRequest request, CancellationToken ct = default)
  {
    CreatedIntents++;

    return Task.FromResult(new GatewayIntent($"pi_{request.Reference}", $"secret_{request.Reference}"));
  }

  public GatewayEvent? ParseEvent(string rawPayload, string? signatureHeader)
  {
    if (signatureHeader != ValidSignature)
      throw new BadOperationAppException("The webhook signature is invalid or expired.");

    // Case-insensitive explícito: por defecto STJ NO lo es, y `eventId` no ataba con
    // `EventId`, así que todo llegaba en null y el webhook parecía funcionar sin hacer nada.
    var body = JsonSerializer.Deserialize<FakeEvent>(rawPayload, CaseInsensitive)
        ?? throw new BadOperationAppException("The webhook body could not be read.");

    return body.Status switch
    {
      "captured" => new GatewayEvent(body.EventId, "payment.succeeded", body.PaymentId, PaymentStatus.Captured, null),
      "failed" => new GatewayEvent(body.EventId, "payment.failed", body.PaymentId, PaymentStatus.Failed, "tarjeta rechazada"),
      _ => null
    };
  }

  private static readonly JsonSerializerOptions CaseInsensitive =
      new() { PropertyNameCaseInsensitive = true };

  private sealed record FakeEvent(string EventId, string PaymentId, string Status);
}
