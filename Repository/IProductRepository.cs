using ApiEcommerce.Models;

namespace ApiEcommerce.Repository;


public interface IProductRepository
{

  Task<ICollection<Product>> GetProducts();
  Task<ICollection<Product>> GetProductsForCategory(int categoryId);
  Task<ICollection<Product>> SearchProduct(string name);
  Task<Product?> GetProduct(int id);


  Task<bool> BuyProduct(string productName, int quantity);


  Task<bool> ProductExists(int id);
  Task<bool> ProductExists(string name);


  Task<bool> CreateProduct(Product product);
  Task<bool> UpdateProduct(Product product);
  Task<bool> DeleteProduct(Product product);

  // commit
  Task<bool> Save();

}
