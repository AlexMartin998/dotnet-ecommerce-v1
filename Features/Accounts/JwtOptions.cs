using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts;


/// <summary>Configuración del emisor de tokens, sección <c>Jwt</c>.</summary>
/// <remarks>
/// Con <c>ValidateOnStart</c>: si falta el secreto o es corto, la app no arranca. Sin ello el
/// fallo aparecería en el primer login, no en el despliegue.
/// </remarks>
public sealed class JwtOptions
{
  /// <summary>Nombre de la sección en configuración.</summary>
  public const string SectionName = "Jwt";

  /// <summary>Quién emite el token (esta API).</summary>
  [Required(ErrorMessage = "Jwt:Issuer is required")]
  public string Issuer { get; init; } = string.Empty;

  /// <summary>Para quién es el token (el front que lo consume).</summary>
  [Required(ErrorMessage = "Jwt:Audience is required")]
  public string Audience { get; init; } = string.Empty;

  /// <summary>Clave simétrica de firma (HMAC-SHA256). Nunca se commitea.</summary>
  /// <remarks>
  /// Mínimo 32 caracteres: HS256 exige 256 bits y con menos .NET lanza en runtime. Va en
  /// user-secrets o en la variable de entorno <c>Jwt__SecretKey</c>.
  /// </remarks>
  [Required(ErrorMessage = "Jwt:SecretKey is required")]
  [MinLength(32, ErrorMessage = "Jwt:SecretKey must be at least 32 characters (256 bits) for HS256")]
  public string SecretKey { get; init; } = string.Empty;

  /// <summary>Vida del access token en minutos.</summary>
  [Range(1, 1440, ErrorMessage = "Jwt:ExpirationMinutes must be between 1 and 1440")]
  public int ExpirationMinutes { get; init; } = 60;
}
