using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts;


/// <summary>Sesiones y rotación de refresh tokens, sección <c>RefreshToken</c>.</summary>
public sealed class RefreshTokenOptions
{
  public const string SectionName = "RefreshToken";

  /// <summary>Cuánto puede durar una sesión sin volver a poner la contraseña.</summary>
  [Range(1, 365)]
  public int LifetimeDays { get; init; } = 14;

  /// <summary>Nombre de la cookie que transporta el refresh token.</summary>
  /// <remarks>
  /// Sin prefijo <c>__Host-</c>: exige <c>Secure</c> y <c>Path=/</c>, y aquí el Path va acotado
  /// y en Development se sirve por HTTP, así que el navegador descartaría la cookie en silencio.
  /// Lo que aporta el prefijo lo cubren <c>SameSite=Strict</c> y no compartir dominio.
  /// </remarks>
  [Required]
  [MaxLength(64)]
  public string CookieName { get; init; } = "rt";

  /// <summary>
  /// Ventana en la que reusar un token recién gastado se toma por una carrera del cliente
  /// y <b>no</b> por un robo.
  /// </summary>
  /// <remarks>
  /// Sin ella la detección de reuso cerraría sesiones legítimas: dos peticiones paralelas que
  /// reciben 401 refrescan con el mismo token. Dentro de la ventana se rechaza igual, pero no
  /// se revoca la familia; el precio es no ver un robo en los primeros segundos.
  /// </remarks>
  [Range(0, 300)]
  public int ReuseGraceSeconds { get; init; } = 15;

  /// <summary><see cref="LifetimeDays"/> como <see cref="TimeSpan"/>.</summary>
  public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);

  /// <summary><see cref="ReuseGraceSeconds"/> como <see cref="TimeSpan"/>.</summary>
  public TimeSpan ReuseGrace => TimeSpan.FromSeconds(ReuseGraceSeconds);
}
