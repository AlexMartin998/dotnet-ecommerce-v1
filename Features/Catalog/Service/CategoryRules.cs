using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Reglas de negocio de <see cref="Category"/>: nombre único y no borrar categorías
/// con productos.
/// </summary>
public sealed class CategoryRules(ICategoryRepository repository)
  : IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>
{
  public string EntityName => "Category";

  public async Task EnsureCanCreateAsync(CreateCategoryDto dto, CancellationToken ct = default)
  {
    if (await repository.NameExistsAsync(dto.Name, ct: ct))
      throw new ConflictAppException($"Category '{dto.Name}' already exists.");

    // Solo choca con nombres que difieren en espacios: el nombre ya es único.
    if (Slugs.From(dto.Name) is { } slug && await repository.SlugExistsAsync(slug, ct))
      throw new ConflictAppException(
          $"The slug '{slug}', derived from the category name, is already in use.");
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
