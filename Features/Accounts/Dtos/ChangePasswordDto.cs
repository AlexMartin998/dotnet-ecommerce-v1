using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Cambio de contraseña del propio usuario.</summary>
public class ChangePasswordDto
{
  /// <summary>
  /// La contraseña actual. <b>Se exige siempre</b>, aunque el usuario ya esté autenticado.
  /// </summary>
  /// <remarks>
  /// Sin ella, quien se siente un minuto delante de una sesión abierta puede cambiar la
  /// contraseña y quedarse con la cuenta. Un access token demuestra que <i>alguien</i>
  /// entró hace un rato, no que quien está ahora sea el dueño.
  /// </remarks>
  [Required]
  public string CurrentPassword { get; set; } = string.Empty;

  [Required]
  [MinLength(8)]
  [MaxLength(128)]
  public string NewPassword { get; set; } = string.Empty;
}
