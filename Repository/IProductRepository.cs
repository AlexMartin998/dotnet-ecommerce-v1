using ApiEcommerce.Models;

namespace ApiEcommerce.Repository;


public interface IProductRepository : IBaseRepository<Product>
{

  Task<IEnumerable<Product>> GetAllWithCategoryAsync(CancellationToken ct = default);


  Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken ct = default);


  Task<ICollection<Product>> GetProductsForCategoryAsync(int categoryId, CancellationToken ct = default);


  Task<ICollection<Product>> SearchProductAsync(string name, CancellationToken ct = default);


  Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default);


  Task<bool> SkuExistsAsync(string sku, int? excludeId = null, CancellationToken ct = default);


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
