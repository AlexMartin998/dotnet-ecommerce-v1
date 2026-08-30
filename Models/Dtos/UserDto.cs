namespace ApiEcommerce.Models.Dtos;


/// <summary>
/// Vista pública de un usuario. <b>Nunca lleva contraseña ni hash</b>, ni siquiera
/// como campo opcional: un DTO que puede transportar una credencial acaba
/// transportándola.
/// </summary>
public class UserDto
{
  public string Id { get; set; } = string.Empty;
  public string Username { get; set; } = string.Empty;
  public string? Email { get; set; }
  public string? Name { get; set; }

  /// <summary>Roles efectivos del usuario ("admin", "user").</summary>
  public IReadOnlyList<string> Roles { get; set; } = [];

  public DateTime CreatedAt { get; set; }
}
