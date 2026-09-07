using System.Security.Cryptography;
using System.Text;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments;
using ApiEcommerce.Features.Payments.Gateways;
using ApiEcommerce.Features.Payments.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Tests.Features.Payments;


/// <summary>La verificación de firma del webhook de Stripe.</summary>
/// <remarks>
/// Es lo único que autentica un webhook: el cuerpo lo manda cualquiera que conozca la URL,
/// así que sin esto marcar un cobro como capturado sería gratis para un atacante. Se prueba
/// firmando a mano con el mismo esquema que usa Stripe, sin salir a Internet.
/// </remarks>
public class StripePaymentGatewayTests
{
  private const string Secret = "whsec_para_estos_tests_0123456789";

  private static StripePaymentGateway Sut(int toleranceSeconds = 300)
      => new(
          Options.Create(new PaymentOptions
          {
            Stripe = new StripeOptions { SecretKey = "sk_test_x", WebhookSecret = Secret, WebhookToleranceSeconds = toleranceSeconds }
          }),
          NullLogger<StripePaymentGateway>.Instance);

  /// <summary>Un evento de Stripe con la forma mínima que el adaptador lee.</summary>
  private static string Payload(string type, string intentId, string eventId = "evt_1") => $$"""
    {
      "id": "{{eventId}}",
      "object": "event",
      "api_version": "2024-06-20",
      "type": "{{type}}",
      "data": { "object": { "id": "{{intentId}}", "object": "payment_intent" } }
    }
    """;

  /// <summary>La cabecera <c>Stripe-Signature</c>, calculada como la calcula Stripe.</summary>
  private static string Sign(string payload, DateTimeOffset? at = null)
  {
    var timestamp = (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();

    using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));

    var signature = Convert.ToHexString(
        hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{payload}"))).ToLowerInvariant();

    return $"t={timestamp},v1={signature}";
  }

  [Fact]
  public void AValidSignatureIsTranslatedToACapture()
  {
    var payload = Payload("payment_intent.succeeded", "pi_123");

    var parsed = Sut().ParseEvent(payload, Sign(payload));

    Assert.NotNull(parsed);
    Assert.Equal("evt_1", parsed!.Value.EventId);
    Assert.Equal("pi_123", parsed.Value.ProviderPaymentId);
    Assert.Equal(PaymentStatus.Captured, parsed.Value.Status);
  }

  [Fact]
  public void AFailedPaymentCarriesItsReason()
  {
    var payload = Payload("payment_intent.payment_failed", "pi_123");

    var parsed = Sut().ParseEvent(payload, Sign(payload));

    Assert.Equal(PaymentStatus.Failed, parsed!.Value.Status);
    Assert.False(string.IsNullOrWhiteSpace(parsed.Value.FailureReason));
  }

  [Fact]
  public void ATamperedBodyIsRejected()
  {
    // La firma es sobre los bytes: cambiar un dígito del importe la invalida. Sin esto,
    // cualquiera que conozca la URL podría darse una compra por pagada.
    var payload = Payload("payment_intent.succeeded", "pi_123");
    var signature = Sign(payload);

    var tampered = payload.Replace("pi_123", "pi_del_atacante");

    Assert.Throws<BadOperationAppException>(() => Sut().ParseEvent(tampered, signature));
  }

  [Fact]
  public void AMissingSignatureIsRejected()
  {
    var payload = Payload("payment_intent.succeeded", "pi_123");

    Assert.Throws<BadOperationAppException>(() => Sut().ParseEvent(payload, null));
  }

  [Fact]
  public void AnOldSignatureIsRejected()
  {
    // Sin ventana temporal, una firma capturada del tráfico valdría para siempre.
    var payload = Payload("payment_intent.succeeded", "pi_123");
    var old = Sign(payload, DateTimeOffset.UtcNow.AddHours(-1));

    Assert.Throws<BadOperationAppException>(() => Sut(toleranceSeconds: 60).ParseEvent(payload, old));
  }

  [Fact]
  public void ABodyThatParsesBadlyIsNeverA500()
  {
    // Encontrados escribiendo estos tests, los dos daban NullReferenceException: un evento
    // sin `api_version` (dentro de ConstructEvent) y uno sin `data` (al leer el objeto).
    // Ninguno es StripeException, así que con un catch estrecho salían como 500 — o sea
    // diciéndole a quien mandó el cuerpo que ha encontrado algo.
    // Los dos van FIRMADOS: llegar hasta aquí ya exige el secreto, y aun así ninguno
    // puede tumbar el proceso. Sin `data` no hay nada que mover, así que se ignoran.
    var sinVersion = """{ "id": "evt_1", "object": "event", "type": "payment_intent.succeeded" }""";

    Assert.Null(Sut().ParseEvent(sinVersion, Sign(sinVersion)));

    var sinData = """
      { "id": "evt_1", "object": "event", "api_version": "2024-06-20", "type": "payment_intent.succeeded" }
      """;

    Assert.Null(Sut().ParseEvent(sinData, Sign(sinData)));
  }

  [Fact]
  public void AnEventWeDoNotCareAboutIsIgnoredAndNotAnError()
  {
    // Ignorar no es fallar: devolver error haría que Stripe reintentara durante días un
    // evento que nunca nos va a mover nada.
    var payload = Payload("payment_intent.created", "pi_123");

    Assert.Null(Sut().ParseEvent(payload, Sign(payload)));
  }
}
