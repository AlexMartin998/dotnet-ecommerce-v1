namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Claves de cache en un solo sitio, para que la que se escribe y la que se invalida se
/// construyan con la misma función.
/// </summary>
/// <remarks>
/// Evita el bug clásico: invalidar una clave que nadie escribió y servir datos rancios
/// para siempre.
/// </remarks>
public static class CacheKeys
{
  /// <summary>Listado completo de categorías.</summary>
  public const string CategoryAll = "category:all";

  /// <summary>Una categoría por id.</summary>
  public static string Category(int id) => $"category:{id}";

  /// <summary>Listado completo de productos.</summary>
  public const string ProductAll = "product:all";

  /// <summary>Un producto por id.</summary>
  public static string Product(int id) => $"product:{id}";

  /// <summary>Productos de una categoría.</summary>
  public static string ProductsByCategory(int categoryId) => $"product:category:{categoryId}";
}
