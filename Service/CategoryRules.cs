using ApiEcommerce.Exceptions;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using ApiEcommerce.Service.Crud;

namespace ApiEcommerce.Service;


/// <summary>
/// Reglas de negocio de <see cref="Category"/>. Es todo lo que Category tiene de
/// propio: el CRUD lo pone <c>CrudService</c>.
/// Se puede instanciar con un <c>ICategoryRepository</c> falso y probar sola.
/// </summary>
public sealed class CategoryRules(ICategoryRepository repository)
  : IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>
{
  public string EntityName => "Category";

  public async Task EnsureCanCreateAsync(CreateCategoryDto dto, CancellationToken ct = default)
  {
    if (await repository.NameExistsAsync(dto.Name, ct: ct))
      throw new ConflictAppException($"Category '{dto.Name}' already exists.");
  }

  public async Task EnsureCanUpdateAsync(
      int id, UpdateCategoryDto dto, Category existing, CancellationToken ct = default)
  {
    // En un PATCH el nombre puede no venir: si no viene, no hay nada que validar.
    if (string.IsNullOrWhiteSpace(dto.Name)) return;

    // excludeId evita que la categoría choque consigo misma
    if (await repository.NameExistsAsync(dto.Name, excludeId: id, ct: ct))
      throw new ConflictAppException($"Category '{dto.Name}' already exists.");
  }

  public async Task EnsureCanDeleteAsync(Category existing, CancellationToken ct = default)
  {
    if (await repository.HasProductsAsync(existing.Id, ct))
      throw new ConflictAppException(
          $"Category '{existing.Name}' can't be deleted because it still has products.");
  }
}
