namespace ApiEcommerce.Service;


public interface IGenericService<T> where T : class
{
  Task<T?> GetByIdAsync(int id);
  Task<IEnumerable<T>> GetAllAsync();

  Task<T> CreateAsync(T entity);
  Task<T> UpdateAsync(T entity);
  Task DeleteAsync(int id);

  Task<bool> ExistsAsync(int id);
  Task<bool> ExistsByFieldAsync(string fieldName, string value);
}
