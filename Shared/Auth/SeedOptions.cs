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

  public string AdminUsername { get; init; } = "admin";

  [EmailAddress]
  public string AdminEmail { get; init; } = "admin@apiecommerce.local";

  /// <summary>Contraseña del admin sembrado. Nunca hardcodeada: user-secrets o <c>Seed__AdminPassword</c>.</summary>
  /// <remarks>
  /// <b>Sin <c>[Required]</c> a propósito.</b> Con DataAnnotations incondicionales, leer
  /// <c>IOptions&lt;SeedOptions&gt;.Value</c> disparaba la validación <b>antes</b> de poder mirar
  /// <see cref="Enabled"/>, así que un despliegue con el seeding APAGADO (que es lo normal
  /// fuera de desarrollo, y no define ninguna contraseña) reventaba el arranque en bucle.
  /// La regla real —"obligatoria solo si el seeding está encendido"— es condicional, y eso
  /// se expresa con <c>.Validate(...)</c> en <c>PersistenceExtensions</c>, no con un atributo.
  /// </remarks>
  public string AdminPassword { get; init; } = string.Empty;

  /// <summary>Si además de roles y admin se siembran categorías y productos de ejemplo.</summary>
  public bool IncludeDemoData { get; init; } = true;
}
