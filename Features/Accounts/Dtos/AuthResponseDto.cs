namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Respuesta de register y login: el token y quién es su dueño.</summary>
public class AuthResponseDto
{
  /// <summary>Access token JWT. Se envía como <c>Authorization: Bearer &lt;token&gt;</c>.</summary>
  public string Token { get; set; } = string.Empty;

  /// <summary>Momento de expiración del token, en hora local como el resto del proyecto.</summary>
  public DateTime ExpiresAt { get; set; }

  /// <summary>Dueño del token.</summary>
  public UserDto User { get; set; } = new();
}
