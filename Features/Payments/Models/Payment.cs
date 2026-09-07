using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Payments.Models;


/// <summary>Con qué se paga. Lo elige el comprador en cada petición.</summary>
/// <remarks>
/// Un enum y no un string libre: el proveedor viaja hasta la ruta del webhook, y aceptar
/// cualquier texto ahí convertiría un error de tecleo en un 500.
/// </remarks>
public enum PaymentProvider
{
  Stripe = 0
}


/// <summary>Estado de un cobro.</summary>
/// <remarks>
/// No hay <c>Authorized</c>: con captura automática ese estado no se alcanza nunca, y un
/// estado inalcanzable es un bug esperando (ya pasó con <c>ReceiptStatus.Failed</c>).
/// </remarks>
public enum PaymentStatus
{
  Pending = 0,
  Captured = 1,
  Failed = 2,
  Cancelled = 3
}


/// <summary>Un intento de cobro contra una pasarela.</summary>
/// <remarks>
/// El importe se copia de la orden, no se referencia: si la orden cambiara, lo que se cobró
/// no puede cambiar con ella. Quien decide que un pago está cobrado es el webhook de la
/// pasarela, nunca la respuesta de nuestra propia API.
/// </remarks>
public class Payment : IAuditable
{
  public int Id { get; set; }

  /// <summary>Referencia legible (<c>PAY-2026-000012</c>). Es lo que cita el cliente.</summary>
  [Required]
  [MaxLength(32)]
  public required string Reference { get; set; }

  public int OrderId { get; set; }

  /// <summary>Copiado de la orden para no tener que unirla en cada listado.</summary>
  [Required]
  [MaxLength(32)]
  public required string OrderNumber { get; set; }

  [Required]
  [MaxLength(450)]
  public required string BuyerUserId { get; set; }

  public PaymentProvider Provider { get; set; }

  /// <summary>El id del intento en la pasarela. Índice único: ata el webhook a esta fila.</summary>
  /// <remarks>
  /// Nullable porque la fila existe antes de que la pasarela conteste; en cuanto hay id, es
  /// único. Repetido haría que un webhook moviera dos pagos.
  /// </remarks>
  [MaxLength(255)]
  public string? ProviderPaymentId { get; set; }

  public decimal Amount { get; set; }

  [Required]
  [MaxLength(3)]
  public string Currency { get; set; } = "USD";

  public PaymentStatus Status { get; set; } = PaymentStatus.Pending;

  /// <summary>Lo que dijo la pasarela al fallar, para poder explicárselo al comprador.</summary>
  [MaxLength(500)]
  public string? FailureReason { get; set; }

  public DateTime CreatedAt { get; set; }

  public DateTime? UpdatedAt { get; set; }
}


/// <summary>Un evento de webhook ya procesado. Deduplicación por el id de la pasarela.</summary>
/// <remarks>
/// Gemelo de <c>ProcessedMessage</c>: las pasarelas reenvían por diseño, así que el webhook
/// es at-least-once igual que el broker. La clave primaria es el árbitro entre réplicas.
/// </remarks>
public class ProcessedWebhookEvent
{
  /// <summary>El id del evento tal y como lo puso la pasarela (<c>evt_…</c>).</summary>
  [MaxLength(255)]
  public required string Id { get; set; }

  public PaymentProvider Provider { get; set; }

  [Required]
  [MaxLength(100)]
  public required string Type { get; set; }

  public DateTime ReceivedAt { get; set; } = DateTime.Now;
}
