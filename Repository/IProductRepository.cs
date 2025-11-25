using ApiEcommerce.Models;

namespace ApiEcommerce.Repository;


public interface IProductRepository: IBaseRepository<Product>
{

  Task<ICollection<Product>> GetProductsForCategory(int categoryId);
  Task<ICollection<Product>> SearchProduct(string name);

  Task<bool> BuyProduct(string productName, int quantity);



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
