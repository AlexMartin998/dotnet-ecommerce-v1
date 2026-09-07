using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Payments.Dtos;


/// <summary>Lo que manda el cliente para empezar a pagar una orden.</summary>
public class StartPaymentDto
{
  [Range(1, int.MaxValue)]
  public int OrderId { get; set; }

  /// <summary>Con qué se paga (<c>stripe</c>). Lo elige el comprador, no el servidor.</summary>
  [Required]
  [MaxLength(30)]
  public string Provider { get; set; } = "stripe";
}


/// <summary>Un pago, tal y como lo ve el cliente.</summary>
public record PaymentDto
{
  public int Id { get; set; }
  public string Reference { get; set; } = string.Empty;
  public int OrderId { get; set; }
  public string OrderNumber { get; set; } = string.Empty;
  public string BuyerUserId { get; set; } = string.Empty;
  public string Provider { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public decimal Amount { get; set; }
  public string Currency { get; set; } = string.Empty;
  public string? FailureReason { get; set; }
  public DateTime CreatedAt { get; set; }

  /// <summary>
  /// Lo que el front necesita para confirmar el cobro. Solo viaja al crear el pago.
  /// </summary>
  /// <remarks>
  /// No se persiste: es un secreto de un solo uso de la pasarela y guardarlo lo convertiría
  /// en algo que se puede filtrar por un listado.
  /// </remarks>
  public string? ClientSecret { get; set; }
}
