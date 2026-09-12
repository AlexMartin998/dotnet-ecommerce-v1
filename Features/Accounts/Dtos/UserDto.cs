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


/// <summary>Contadores de usuarios, para el panel de administración.</summary>
public class UserStatsDto
{
  public int Total { get; set; }

  /// <summary>Cuántos tienen el rol administrador.</summary>
  public int Admins { get; set; }

  /// <summary>Cuentas con el bloqueo VIGENTE, no las que alguna vez se bloquearon.</summary>
  public int Locked { get; set; }
}
