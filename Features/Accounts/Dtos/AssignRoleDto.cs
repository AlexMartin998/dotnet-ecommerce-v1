using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Rol que se asigna a un usuario.</summary>
public class AssignRoleDto
{
  /// <summary>Nombre del rol. Debe existir: no se crean al vuelo.</summary>
  [Required]
  [MaxLength(64)]
  public string Role { get; set; } = string.Empty;
}
