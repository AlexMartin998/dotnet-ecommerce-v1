namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Claves de cache en un solo sitio. Que la clave que se <b>escribe</b> y la que se
/// <b>invalida</b> se construyan con la misma función es lo que evita el bug clásico
/// de cache: invalidar una clave que nadie escribió y servir datos rancios para siempre.
/// </summary>
public static class CacheKeys
{
  public const string CategoryAll = "category:all";

  public static string Category(int id) => $"category:{id}";

  public const string ProductAll = "product:all";

  public static string Product(int id) => $"product:{id}";

  public static string ProductsByCategory(int categoryId) => $"product:category:{categoryId}";
}
