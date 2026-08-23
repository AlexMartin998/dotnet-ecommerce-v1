using ApiEcommerce.Models;

namespace ApiEcommerce.Repository;


public interface ICategoryRepository : IBaseRepository<Category>
{

  Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default);


  Task<bool> HasProductsAsync(int categoryId, CancellationToken ct = default);

}
