using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Models;


/// <summary>
/// Un refresh token emitido: lo que permite alargar una sesión, y lo que permite cortarla.
/// </summary>
/// <remarks>
/// <para>
/// Es la <b>garantía</b> de que una sesión se puede revocar, y por eso vive en la base de
/// datos y no en una cache: revocarla y emitir el siguiente ocurren en la misma
/// transacción. La denylist de <c>jti</c> en Redis es otra cosa —una optimización para que
/// el access token que el cliente ya tiene muera en el acto— y puede faltar sin que la
/// sesión deje de cortarse.
/// </para>
/// </remarks>
public class RefreshToken
{
  public int Id { get; set; }

  /// <summary>
  /// <b>SHA-256 del token</b>, en hexadecimal. Nunca el token en claro.
  /// </summary>
  /// <remarks>
  /// Misma lógica que una contraseña: si la base se filtra, lo que hay dentro no sirve
  /// para autenticarse. Y como el índice es único, es además lo que hace la búsqueda
  /// barata sin tener que descifrar nada.
  /// ⚠️ Con <c>MaxLength</c>, no <c>nvarchar(max)</c>: eso no es indexable en SQL Server.
  /// </remarks>
  [Required]
  [MaxLength(64)]
  public required string TokenHash { get; set; }

  [Required]
  [MaxLength(450)]
  public required string UserId { get; set; }

  /// <summary>
  /// Identifica la <b>cadena</b> de tokens que nace en un login y se va rotando.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Es lo que permite revocar de golpe toda la descendencia cuando se detecta un reuso.
  /// La alternativa —seguir el rastro de <c>ReplacedBy</c> token a token— exige recorrer
  /// la cadena entera con una consulta por eslabón, y basta con que falte uno para dejar
  /// media sesión viva.
  /// </para>
  /// <para>
  /// Un usuario tiene una familia <b>por sesión</b>: cerrar sesión en el móvil no cierra
  /// la del portátil, que es lo que uno espera.
  /// </para>
  /// </remarks>
  public Guid FamilyId { get; set; }

  public DateTime ExpiresAt { get; set; }

  /// <summary>Cuándo se gastó o se revocó. <c>null</c> = sigue vivo.</summary>
  public DateTime? RevokedAt { get; set; }

  /// <summary>
  /// IP desde la que se emitió. Solo para investigar un incidente.
  /// </summary>
  /// <remarks>
  /// No se usa para decidir nada: detrás de un proxy o de una red móvil la IP cambia
  /// sola, y atar la sesión a ella corta a usuarios legítimos constantemente.
  /// </remarks>
  [MaxLength(64)]
  public string? CreatedByIp { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.Now;

  /// <summary>¿Sigue siendo utilizable?</summary>
  public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
