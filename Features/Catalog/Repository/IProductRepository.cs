using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Repository;


/// <summary>
/// CRUD de productos más las consultas propias del dominio. Las variantes
/// <c>...WithCategoryAsync</c> existen porque sin el <c>Include</c> de la navegación
/// <c>ProductDto.CategoryName</c> sale vacío en silencio.
/// </summary>
public interface IProductRepository : IBaseRepository<Product>
{

  /// <summary>Todos los productos, con su categoría cargada.</summary>
  Task<IEnumerable<Product>> GetAllWithCategoryAsync(CancellationToken ct = default);


  /// <summary>Un producto por id, con su categoría cargada.</summary>
  Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken ct = default);


  /// <summary>Página de productos, con su categoría cargada.</summary>
  Task<PagedResult<Product>> GetPagedWithCategoryAsync(int page, int pageSize, CancellationToken ct = default);


  /// <summary>Productos de una categoría, con su categoría cargada.</summary>
  Task<ICollection<Product>> GetProductsForCategoryAsync(int categoryId, CancellationToken ct = default);


  /// <summary>Productos cuyo nombre contiene <paramref name="name"/>.</summary>
  Task<ICollection<Product>> SearchProductAsync(string name, CancellationToken ct = default);


  /// <summary>Un producto por SKU, normalizando espacios y mayúsculas.</summary>
  Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);


  /// <summary>¿Hay ya otro producto con ese SKU? <paramref name="excludeId"/> se excluye de la comprobación.</summary>
  Task<bool> SkuExistsAsync(string sku, int? excludeId = null, CancellationToken ct = default);


  /// <summary>
  /// Descuenta stock de forma atómica en un único
  /// <c>UPDATE ... SET Stock = Stock - @q WHERE Id = @id AND Stock &gt;= @q</c>.
  /// Devuelve <c>false</c> si no había stock suficiente.
  /// </summary>
  /// <remarks>
  /// Devuelve <c>bool</c> y no lanza: quien decide que eso es un 409 es el servicio.
  /// Para un contador con contención esto es mejor que la concurrencia optimista, que
  /// rechazaría compras válidas al agotar los reintentos.
  /// </remarks>
  Task<bool> TryDecrementStockAsync(int productId, int quantity, CancellationToken ct = default);

  /// <summary>Carga el producto rastreado junto a sus imágenes, para poder añadir o quitar.</summary>
  Task<Product?> GetByIdWithImagesAsync(int id, CancellationToken ct = default);

  /// <summary>Retira el producto del catálogo sin borrar la fila.</summary>
  /// <remarks>
  /// Borrado lógico porque las líneas de orden apuntan al producto por clave foránea: un
  /// borrado real las dejaría sin referencia o fallaría. Un filtro global lo esconde de
  /// todas las consultas a partir de aquí.
  /// </remarks>
  /// <returns><c>true</c> si esta llamada fue la que lo retiró.</returns>
  Task<bool> SoftDeleteAsync(int id, CancellationToken ct = default);

  /// <summary>¿Hay ya un producto vivo con ese slug?</summary>
  Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

  /// <summary>El producto vivo con ese slug, con su categoría y sus imágenes.</summary>
  Task<Product?> GetBySlugAsync(string slug, CancellationToken ct = default);

  /// <summary>Total de productos, agotados y con stock bajo, en la base.</summary>
  /// <remarks>
  /// <paramref name="lowStockThreshold"/> es el tope de «bajo», y el agotado no cuenta como
  /// tal. Se cuenta en SQL: traer el catálogo para contarlo no escala.
  /// </remarks>
  Task<(int Total, int OutOfStock, int LowStock)> CountStockAsync(
      int lowStockThreshold, CancellationToken ct = default);

  /// <summary>Devuelve stock al catálogo. Suma incondicional.</summary>
  /// <remarks>
  /// Sin condición porque no la hay: devolver siempre puede. Quien decide que se puede
  /// devolver es la transición de estado de quien llama, no esta sentencia.
  /// </remarks>
  Task IncrementStockAsync(int productId, int quantity, CancellationToken ct = default);


  // // Sustituido por GetBySkuAsync + la regla en ProductService.BuyAsync: el `bool`
  // // mezclaba "no existe" (404) con "stock insuficiente" (409).
  // Task<bool> BuyProduct(string productName, int quantity);


  // Task<ICollection<Product>> GetProducts();
  // Task<Product?> GetProduct(int id);

  // Task<bool> ProductExists(int id);
  // Task<bool> ProductExists(string name);

  // Task<bool> CreateProduct(Product product);
  // Task<bool> UpdateProduct(Product product);
  // Task<bool> DeleteProduct(Product product);

  // // commit
  // Task<bool> Save();

}
