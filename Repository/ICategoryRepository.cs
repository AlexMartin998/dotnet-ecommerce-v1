using ApiEcommerce.Models;

namespace ApiEcommerce.Repository;


public interface ICategoryRepository : IBaseRepository<Category>
{
  Task<bool> NameExistsAsync(string name);
}
