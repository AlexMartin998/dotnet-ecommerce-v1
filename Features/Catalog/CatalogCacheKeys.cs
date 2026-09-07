namespace ApiEcommerce.Features.Catalog;


/// <summary>
/// Claves de cache del catálogo, en un solo sitio para que la que se escribe y la que se
/// invalida se construyan con la misma función.
/// </summary>
/// <remarks>
/// Vive en el slice y no en <c>Shared/Caching</c>: «category» es vocabulario del catálogo,
/// y el mecanismo de cache no tiene por qué conocerlo. Evita el bug clásico de invalidar
/// una clave que nadie escribió y servir datos rancios para siempre.
/// </remarks>
public static class CatalogCacheKeys
{
  public const string CategoryAll = "category:all";

  public static string Category(int id) => $"category:{id}";
}
