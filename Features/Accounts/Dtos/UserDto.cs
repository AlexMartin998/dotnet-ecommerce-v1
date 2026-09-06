namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Vista pública de un usuario. Nunca lleva contraseña ni hash.</summary>
public class UserDto
{
  /// <summary>Identificador del usuario.</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>Nombre de login.</summary>
  public string Username { get; set; } = string.Empty;

  /// <summary>Correo del usuario.</summary>
  public string? Email { get; set; }

  /// <summary>Nombre para mostrar.</summary>
  public string? Name { get; set; }

  /// <summary>Roles efectivos del usuario ("admin", "user").</summary>
  public IReadOnlyList<string> Roles { get; set; } = [];

  /// <summary>Fecha de alta.</summary>
  public DateTime CreatedAt { get; set; }
}
