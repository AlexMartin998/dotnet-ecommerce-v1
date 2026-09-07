using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Payments;


/// <summary>Configuración del contexto de pagos (sección <c>Payments</c>).</summary>
public sealed class PaymentOptions
{
  public const string SectionName = "Payments";

  /// <summary>Cuánto se le guarda el stock a una orden sin pagar.</summary>
  /// <remarks>
  /// La orden nace <c>Placed</c> con el stock ya apartado; pasado este plazo sin cobro, el
  /// recolector la cancela y lo devuelve. Cero lo APAGA, y entonces una orden abandonada
  /// retiene su stock para siempre.
  /// </remarks>
  [Range(0, 10_080)]
  public int ReservationMinutes { get; init; } = 30;

  /// <summary>Cada cuánto pasa el recolector de órdenes abandonadas. Cero lo apaga.</summary>
  [Range(0, 1440)]
  public int CleanupIntervalMinutes { get; init; } = 10;

  public StripeOptions Stripe { get; init; } = new();
}


/// <summary>Credenciales de Stripe. Vacías = el proveedor no se registra.</summary>
/// <remarks>
/// ⚠️ Nunca se commitean: van en user-secrets o en variables de entorno. Sin
/// <see cref="WebhookSecret"/> no se puede verificar ninguna firma, así que el webhook
/// quedaría abierto a cualquiera que conozca la URL; por eso las dos van juntas o no va
/// ninguna.
/// </remarks>
public sealed class StripeOptions
{
  public string SecretKey { get; init; } = string.Empty;

  public string WebhookSecret { get; init; } = string.Empty;

  /// <summary>Margen de reloj al validar la marca de tiempo de la firma, en segundos.</summary>
  [Range(30, 3600)]
  public int WebhookToleranceSeconds { get; init; } = 300;

  /// <summary>Está configurado del todo, o no está.</summary>
  public bool IsConfigured
      => !string.IsNullOrWhiteSpace(SecretKey) && !string.IsNullOrWhiteSpace(WebhookSecret);
}
