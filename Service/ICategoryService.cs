using ApiEcommerce.Models.Dtos;

namespace ApiEcommerce.Service;


public interface ICategoryService
{

  Task<IEnumerable<CategoryDto>> GetAllAsync();
  Task<CategoryDto?> GetByIdAsync(int id);
  Task<int> CreateAsync(CreateCategoryDto dto);
  Task UpdateAsync(int id, CreateCategoryDto dto);
  Task DeleteAsync(int id);

}
