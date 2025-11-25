using ApiEcommerce.Repository;

namespace ApiEcommerce.Service;



public class GenericService<T>(IBaseRepository<T> repository) : IGenericService<T> where T : class
{

  private readonly IBaseRepository<T> _repository = repository;

  public Task<T?> GetByIdAsync(int id) => _repository.GetByIdAsync(id);

  public Task<IEnumerable<T>> GetAllAsync() => _repository.GetAllAsync();

  public Task<T> CreateAsync(T entity) => _repository.AddAsync(entity);

  public Task<T> UpdateAsync(T entity) => _repository.UpdateAsync(entity);

  public Task DeleteAsync(int id) => _repository.DeleteAsync(id);

  public Task<bool> ExistsAsync(int id) => _repository.ExistsAsync(id);

  public Task<bool> ExistsByFieldAsync(string fieldName, string value)
      => _repository.ExistsByFieldAsync(fieldName, value);
}
