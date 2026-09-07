using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Ordering.Events;


/// <summary>Cómo ve Ordering el <c>payment.captured</c> que emite el contexto de pagos.</summary>
/// <remarks>
/// ⚠️ Es una copia deliberada, no un descuido: el contrato de un evento de integración es
/// su <b>JSON</b>, no una clase .NET compartida. Referenciar el tipo de Payments ataría los
/// dos slices al mismo ensamblado y haría que renombrar un campo allí rompiera aquí en
/// tiempo de compilación en vez de en el contrato, que es donde se debe notar.
/// Solo se declaran los campos que Ordering usa; el resto se ignora al deserializar.
/// </remarks>
public sealed record PaymentCapturedNotice(
    int PaymentId,
    string Reference,
    int OrderId,
    string OrderNumber,
    decimal Amount,
    string Currency,
    DateTime CapturedAt) : IDomainEvent
{
  /// <summary>La misma routing key que publica Payments. Es lo único que los une.</summary>
  public static string EventType => "payment.captured";
}
