using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Features.Payments.Ports;
using Microsoft.Extensions.Options;
using Stripe;

namespace ApiEcommerce.Features.Payments.Gateways;


/// <summary>La pasarela de Stripe, con PaymentIntents y captura automática.</summary>
/// <remarks>
/// Es el único archivo del proyecto que nombra tipos de <c>Stripe.net</c>: cambiar de
/// proveedor o añadir otro no toca dominio, servicio ni controller.
/// </remarks>
public sealed class StripePaymentGateway(
    IOptions<PaymentOptions> options, ILogger<StripePaymentGateway> logger) : IPaymentGateway
{
  private readonly StripeOptions _options = options.Value.Stripe;

  public PaymentProvider Provider => PaymentProvider.Stripe;

  public async Task<GatewayIntent> CreateIntentAsync(
      PaymentRequest request, CancellationToken ct = default)
  {
    var service = new PaymentIntentService(new StripeClient(_options.SecretKey));

    PaymentIntent intent;

    try
    {
      intent = await service.CreateAsync(
          new PaymentIntentCreateOptions
        {
          // Stripe cobra en la unidad mínima: 12,34 USD son 1234. Enviar 12,34 cobraría 12 centavos.
          Amount = ToMinorUnits(request.Amount),
          Currency = request.Currency.ToLowerInvariant(),
          Description = $"Order {request.OrderNumber}",
          ReceiptEmail = request.BuyerEmail,
          // Vuelve en el webhook: es lo que permite conciliar sin confiar en el cuerpo.
          Metadata = new Dictionary<string, string>
          {
            ["payment_reference"] = request.Reference,
            ["order_number"] = request.OrderNumber
          },
          AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true }
        },
        // La misma referencia da el mismo intento: sin esto, un reintento de red crearía dos
        // cobros en Stripe aunque de este lado solo hubiera una fila.
          new RequestOptions { IdempotencyKey = request.Reference },
          ct);
    }
    catch (StripeException ex)
    {
      // 503 y no 500: que Stripe no conteste no es un fallo NUESTRO, es reintentable, y el
      // cliente necesita saber que puede volver a intentarlo. Como `internal_error` el
      // comprador solo veía "ha ocurrido un error inesperado" y nadie sabía de quién era.
      // El motivo real se queda en el log: puede nombrar la clave o el importe.
      logger.LogError(ex, "Stripe rejected the payment intent for {Reference}", request.Reference);

      throw new CustomAppException(
          "payment_gateway_unavailable",
          "The payment provider is not responding. Try again in a moment.",
          System.Net.HttpStatusCode.ServiceUnavailable);
    }

    return new GatewayIntent(intent.Id, intent.ClientSecret);
  }

  public GatewayEvent? ParseEvent(string rawPayload, string? signatureHeader)
  {
    Stripe.Event stripeEvent;

    try
    {
      // Verifica HMAC y marca de tiempo a la vez. Sin la tolerancia, una firma capturada
      // valdría para siempre.
      stripeEvent = EventUtility.ConstructEvent(
          rawPayload, signatureHeader, _options.WebhookSecret, _options.WebhookToleranceSeconds,
          // Stripe sube su API sin avisar y un evento de otra versión sigue siendo válido:
          // rechazarlo dejaría de procesar cobros el día que ellos publiquen.
          throwOnApiVersionMismatch: false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Toda la cadena —firma, plazo y parseo— sobre un cuerpo que manda cualquiera, así
      // que cualquier fallo aquí es petición mala y no error del servidor. Sin este catch
      // ancho, un JSON deforme sale como 500 y le dice al atacante que ha encontrado algo.
      // No se registra el cuerpo: puede llevar datos de tarjeta.
      logger.LogWarning("Rejected a Stripe webhook: {Reason}", ex.Message);

      throw new BadOperationAppException("The webhook signature is invalid or expired.");
    }

    // `Data?` y no `Data.`: un cuerpo sin `data` es JSON válido y dejaba caer una
    // NullReferenceException, o sea un 500 donde debería haber un 400.
    if (stripeEvent.Data?.Object is not PaymentIntent intent)
      return null;

    // Solo estos dos mueven un pago; el resto del catálogo de Stripe se ignora en silencio,
    // que es distinto de fallar: devolver error haría que Stripe reintentara para siempre.
    return stripeEvent.Type switch
    {
      "payment_intent.succeeded" => new GatewayEvent(
          stripeEvent.Id, stripeEvent.Type, intent.Id, PaymentStatus.Captured, null),

      "payment_intent.payment_failed" => new GatewayEvent(
          stripeEvent.Id, stripeEvent.Type, intent.Id, PaymentStatus.Failed,
          intent.LastPaymentError?.Message ?? "The payment was declined."),

      _ => null
    };
  }

  /// <summary>Convierte a la unidad mínima de la moneda (centavos).</summary>
  /// <remarks>
  /// ⚠️ Vale para las monedas de dos decimales. JPY o KRW no tienen fracción y esto las
  /// cobraría cien veces; hoy solo se factura en USD, y el día que no, esto es lo primero.
  /// </remarks>
  private static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m);
}
