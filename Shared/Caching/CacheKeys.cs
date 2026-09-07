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
  public const string CategoryAll = "category:all";

  public static string Category(int id) => $"category:{id}";

  public const string ProductAll = "product:all";

  public static string Product(int id) => $"product:{id}";

  public static string ProductsByCategory(int categoryId) => $"product:category:{categoryId}";
}
