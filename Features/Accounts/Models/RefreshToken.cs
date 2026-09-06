using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Models;


/// <summary>
/// Un refresh token emitido: lo que permite alargar una sesión, y lo que permite cortarla.
/// </summary>
/// <remarks>
/// Vive en la base y no en una cache porque es la garantía de que una sesión se puede revocar:
/// revocar el viejo y emitir el siguiente van en la misma transacción. La denylist de
/// <c>jti</c> en Redis es solo una optimización y puede faltar.
/// </remarks>
public class RefreshToken
{
  /// <summary>Clave primaria.</summary>
  public int Id { get; set; }

  /// <summary>SHA-256 del token en hexadecimal. Nunca el token en claro.</summary>
  /// <remarks>
  /// Si la base se filtra, lo que hay dentro no sirve para autenticarse. Con <c>MaxLength</c>
  /// y no <c>nvarchar(max)</c>, que no es indexable en SQL Server.
  /// </remarks>
  [Required]
  [MaxLength(64)]
  public required string TokenHash { get; set; }

  /// <summary>Dueño de la sesión.</summary>
  [Required]
  [MaxLength(450)]
  public required string UserId { get; set; }

  /// <summary>Identifica la cadena de tokens que nace en un login y se va rotando.</summary>
  /// <remarks>
  /// Permite revocar de golpe toda la descendencia al detectar un reuso, sin recorrer la cadena
  /// eslabón a eslabón. Hay una familia por sesión: cerrar en el móvil no cierra el portátil.
  /// </remarks>
  public Guid FamilyId { get; set; }

  /// <summary>Cuándo deja de valer.</summary>
  public DateTime ExpiresAt { get; set; }

  /// <summary>Cuándo se gastó o se revocó. <c>null</c> = sigue vivo.</summary>
  public DateTime? RevokedAt { get; set; }

  /// <summary>IP desde la que se emitió. Solo para investigar un incidente.</summary>
  /// <remarks>
  /// No se usa para decidir nada: detrás de un proxy o de una red móvil la IP cambia sola.
  /// </remarks>
  [MaxLength(64)]
  public string? CreatedByIp { get; set; }

  /// <summary>Cuándo se emitió.</summary>
  public DateTime CreatedAt { get; set; } = DateTime.Now;

  /// <summary>¿Sigue siendo utilizable?</summary>
  public bool IsActive(DateTime now) => RevokedAt is null && ExpiresAt > now;
}
