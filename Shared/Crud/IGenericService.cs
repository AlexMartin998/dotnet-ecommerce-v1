using ApiEcommerce.Shared.Persistence;
namespace ApiEcommerce.Shared.Crud;


// LEGACY — se conserva comentado como registro de aprendizaje.
//
// Primer intento de "servicio genérico". El problema: opera sobre ENTIDADES, no
// sobre DTOs, así que exponerlo a un controller filtraría el modelo de EF hacia
// fuera de la capa de servicio (ver AGENTS/docs/01-capas-y-contratos.md). Además
// era un passthrough puro a IBaseRepository<T>: una capa sin ninguna decisión.
//
// Lo sustituye ICrudService<TDto, TCreateDto, TUpdateDto> (Service/Crud/), que
// habla DTOs y compone las reglas de negocio de cada entidad.
//
// public interface IGenericService<T> where T : class
// {
//   Task<T?> GetByIdAsync(int id);
//   Task<IEnumerable<T>> GetAllAsync();
//
//   Task<T> CreateAsync(T entity);
//   Task<T> UpdateAsync(T entity);
//   Task DeleteAsync(int id);
//
//   Task<bool> ExistsAsync(int id);
//   Task<bool> ExistsByFieldAsync(string fieldName, string value);
// }
