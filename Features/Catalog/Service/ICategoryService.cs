
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Servicio de categorías. El CRUD lo aporta <see cref="ICrudService{TDto, TCreateDto, TUpdateDto}"/>
/// y aquí solo se declara lo propio del dominio (hoy, nada). La entidad no aparece: el
/// controller no puede verla.
/// </summary>
public interface ICategoryService
  : ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>
{
}
