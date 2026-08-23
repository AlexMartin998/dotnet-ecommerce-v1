using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Service.Crud;

namespace ApiEcommerce.Service;


/// <summary>
/// Servicio de categorías. El CRUD lo aporta <see cref="ICrudService{TDto, TCreateDto, TUpdateDto}"/>;
/// aquí solo se declara lo propio del dominio (hoy, nada).
/// Nótese que la entidad <c>Category</c> no aparece: el controller no puede verla.
/// </summary>
public interface ICategoryService
  : ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>
{
}
