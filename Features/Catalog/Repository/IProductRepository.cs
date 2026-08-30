using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Repository;


public interface IProductRepository : IBaseRepository<Product>
{

  Task<IEnumerable<Product>> GetAllWithCategoryAsync(CancellationToken ct = default);


  Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken ct = default);


  /// <summary>
  /// Página de productos <b>con la categoría cargada</b>. Existe aparte del
  /// <c>GetPagedAsync</c> genérico por la misma razón que <c>GetAllWithCategoryAsync</c>:
  /// sin el <c>Include</c>, <c>ProductDto.CategoryName</c> sale vacío en silencio.
  /// </summary>
  Task<PagedResult<Product>> GetPagedWithCategoryAsync(int page, int pageSize, CancellationToken ct = default);


  Task<ICollection<Product>> GetProductsForCategoryAsync(int categoryId, CancellationToken ct = default);


  Task<ICollection<Product>> SearchProductAsync(string name, CancellationToken ct = default);


  Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);


  Task<bool> SkuExistsAsync(string sku, int? excludeId = null, CancellationToken ct = default);


  /// <summary>
  /// Descuenta stock de forma <b>atómica</b>: un único
  /// <c>UPDATE ... SET Stock = Stock - @q WHERE Id = @id AND Stock >= @q</c>.
  /// Devuelve <c>false</c> si no había stock suficiente.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Devuelve <c>bool</c> y no lanza: el repositorio informa de un hecho ("no se
  /// pudo descontar"), y es el servicio quien decide que eso es un 409.
  /// </para>
  /// <para>
  /// Es la herramienta correcta para un <b>contador</b>. La concurrencia optimista
  /// (<c>Product.RowVersion</c>) sirve para <i>editar</i> una entidad —dos admins
  /// tocando el mismo producto—, pero aplicada a un contador con mucha contención
  /// hace que peticiones perfectamente válidas se rechacen al agotar los reintentos.
  /// Aquí no hay nada que reintentar: la propia base evalúa la condición y decrementa
  /// en la misma sentencia.
  /// </para>
  /// </remarks>
  Task<bool> TryDecrementStockAsync(int productId, int quantity, CancellationToken ct = default);


  // // Sustituido por GetBySkuAsync + la regla de negocio en ProductService.BuyAsync:
  // // devolver un `bool` mezclaba "no existe" (404) con "stock insuficiente" (409)
  // // y obligaba al repositorio a aplicar una regla que no le corresponde.
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
