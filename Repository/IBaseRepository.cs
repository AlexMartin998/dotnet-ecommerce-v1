namespace ApiEcommerce.Repository;


public interface IBaseRepository<T> where T : class
{

  Task<T?> GetByIdAsync(int id);
  Task<IEnumerable<T>> GetAllAsync();
  Task<T> AddAsync(T entity);
  Task<T> UpdateAsync(T entity);
  Task DeleteAsync(int id);
  Task<bool> ExistsAsync(int id);
  Task<bool> ExistsByFieldAsync(string fieldName, string value);

}