namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Vista pública de un usuario. Nunca lleva contraseña ni hash.</summary>
public class UserDto
{
  public string Id { get; set; } = string.Empty;

  public string Username { get; set; } = string.Empty;

  public string? Email { get; set; }

  public string? Name { get; set; }

  public IReadOnlyList<string> Roles { get; set; } = [];

  public DateTime CreatedAt { get; set; }
}
