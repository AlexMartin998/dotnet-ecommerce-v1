using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Configuración del emisor de tokens, enlazada desde la sección <c>Jwt</c> de
/// <c>appsettings.json</c>. Es el equivalente tipado de <c>@ConfigurationProperties</c>
/// de Spring Boot.
/// </summary>
/// <remarks>
/// Se registra con <c>ValidateDataAnnotations().ValidateOnStart()</c>: si falta el
/// secreto o es demasiado corto, <b>la app no arranca</b>. Es deliberado — un secreto
/// vacío no rompe en el arranque sino en el primer login, en producción, de noche.
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

  /// <summary>
  /// Clave simétrica de firma (HMAC-SHA256). Mínimo 32 caracteres porque
  /// HS256 exige una clave de al menos 256 bits; con menos, .NET lanza en runtime.
  /// <b>Nunca se commitea</b>: va en user-secrets o en la variable de entorno
  /// <c>Jwt__SecretKey</c>.
  /// </summary>
  [Required(ErrorMessage = "Jwt:SecretKey is required")]
  [MinLength(32, ErrorMessage = "Jwt:SecretKey must be at least 32 characters (256 bits) for HS256")]
  public string SecretKey { get; init; } = string.Empty;

  /// <summary>Vida del access token en minutos.</summary>
  [Range(1, 1440, ErrorMessage = "Jwt:ExpirationMinutes must be between 1 and 1440")]
  public int ExpirationMinutes { get; init; } = 60;
}
