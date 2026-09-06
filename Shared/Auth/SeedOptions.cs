using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Auth;


/// <summary>Datos de arranque (sección <c>Seed</c>). Desactivado por defecto.</summary>
/// <remarks>
/// Que <see cref="Enabled"/> sea <c>false</c> por defecto es deliberado: el seeder del
/// código de referencia sembraba un <c>admin</c> con contraseña conocida también en
/// producción.
/// </remarks>
public sealed class SeedOptions
{
  /// <summary>Nombre de la sección de configuración.</summary>
  public const string SectionName = "Seed";

  /// <summary>Si se siembran datos al arrancar.</summary>
  public bool Enabled { get; init; }

  /// <summary>Usuario del admin sembrado.</summary>
  public string AdminUsername { get; init; } = "admin";

  /// <summary>Email del admin sembrado.</summary>
  /// <remarks>
  /// Sin <c>[EmailAddress]</c>: una anotación incondicional se evalúa al leer
  /// <c>.Value</c>, antes de poder mirar <see cref="Enabled"/>, y tumbaba el arranque de un
  /// despliegue con el seeding apagado. Si la regla es condicional, va en <c>.Validate(...)</c>.
  /// </remarks>
  public string AdminEmail { get; init; } = "admin@apiecommerce.local";

  /// <summary>Contraseña del admin sembrado. Nunca hardcodeada: user-secrets o <c>Seed__AdminPassword</c>.</summary>
  /// <remarks>
  /// Sin <c>[Required]</c> por el mismo motivo que <see cref="AdminEmail"/>: la regla real
  /// («obligatoria solo si el seeding está encendido») se expresa con <c>.Validate(...)</c>
  /// en <c>PersistenceExtensions</c>.
  /// </remarks>
  public string AdminPassword { get; init; } = string.Empty;

  /// <summary>Si además de roles y admin se siembran categorías y productos de ejemplo.</summary>
  public bool IncludeDemoData { get; init; } = true;
}
