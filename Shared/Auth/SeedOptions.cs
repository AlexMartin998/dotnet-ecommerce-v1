using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Auth;


/// <summary>Datos de arranque (sección <c>Seed</c>). Desactivado por defecto.</summary>
/// <remarks>
/// Que <see cref="Enabled"/> sea <c>false</c> por defecto es deliberado: el seeder del
/// código de referencia se ejecutaba sin ninguna guarda de entorno y sembraba un
/// usuario <c>admin</c> con contraseña conocida <b>también en producción</b>.
/// Aquí hay que encenderlo a propósito y la contraseña sale de configuración.
/// </remarks>
public sealed class SeedOptions
{
  public const string SectionName = "Seed";

  public bool Enabled { get; init; }

  [Required]
  public string AdminUsername { get; init; } = "admin";

  [Required, EmailAddress]
  public string AdminEmail { get; init; } = "admin@apiecommerce.local";

  /// <summary>Contraseña del admin sembrado. Nunca hardcodeada: user-secrets o <c>Seed__AdminPassword</c>.</summary>
  [Required, MinLength(8)]
  public string AdminPassword { get; init; } = string.Empty;

  /// <summary>Si además de roles y admin se siembran categorías y productos de ejemplo.</summary>
  public bool IncludeDemoData { get; init; } = true;
}
