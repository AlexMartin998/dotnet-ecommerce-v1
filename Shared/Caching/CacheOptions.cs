using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Caching;


/// <summary>Configuración de la cache distribuida, sección <c>Redis</c> de <c>appsettings.json</c>.</summary>
public sealed class CacheOptions
{
  /// <summary>Nombre de la sección de configuración.</summary>
  public const string SectionName = "Redis";

  /// <summary>
  /// Cadena de conexión de StackExchange.Redis (<c>host:puerto</c>). Vacía desactiva la
  /// cache y la app arranca igual: es una optimización, no una dependencia dura.
  /// </summary>
  public string Configuration { get; init; } = string.Empty;

  /// <summary>
  /// Prefijo de todas las claves, para compartir una instancia de Redis entre varias apps
  /// sin que se pisen.
  /// </summary>
  public string InstanceName { get; init; } = "apiecommerce:";

  /// <summary>TTL por defecto cuando quien llama no especifica uno.</summary>
  [Range(1, 86400)]
  public int DefaultTtlSeconds { get; init; } = 60;

  /// <summary>Atajo para saber si hay que registrar Redis o el no-op.</summary>
  public bool IsEnabled => !string.IsNullOrWhiteSpace(Configuration);
}
