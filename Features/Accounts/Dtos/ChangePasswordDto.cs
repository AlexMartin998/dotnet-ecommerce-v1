using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Cambio de contraseña del propio usuario.</summary>
public class ChangePasswordDto
{
  /// <summary>La contraseña actual. Se exige siempre, aunque ya esté autenticado.</summary>
  /// <remarks>
  /// Sin ella, quien alcance una sesión abierta un minuto podría quedarse con la cuenta.
  /// </remarks>
  [Required]
  public string CurrentPassword { get; set; } = string.Empty;

  /// <summary>La contraseña nueva. Debe cumplir además la política de Identity.</summary>
  [Required]
  [MinLength(8)]
  [MaxLength(128)]
  public string NewPassword { get; set; } = string.Empty;
}
