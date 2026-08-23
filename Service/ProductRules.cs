using ApiEcommerce.Exceptions;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using ApiEcommerce.Service.Crud;

namespace ApiEcommerce.Service;


/// <summary>
/// Reglas de negocio de <see cref="Product"/>: SKU único y categoría existente.
/// </summary>
/// <remarks>
/// Validar la FK aquí es obligatorio: si se deja pasar un <c>CategoryId</c>
/// inexistente, EF revienta con un error de clave foránea y el cliente recibe un
/// 500 en vez del 400 que corresponde.
/// </remarks>
public sealed class ProductRules(
    IProductRepository productRepository,
    ICategoryRepository categoryRepository)
  : IEntityRules<Product, CreateProductDto, UpdateProductDto>
{
  public string EntityName => "Product";

  public async Task EnsureCanCreateAsync(CreateProductDto dto, CancellationToken ct = default)
  {
    await EnsureCategoryExistsAsync(dto.CategoryId, ct);
    await EnsureSkuIsFreeAsync(dto.SKU, excludeId: null, ct);
  }

  public async Task EnsureCanUpdateAsync(
      int id, UpdateProductDto dto, Product existing, CancellationToken ct = default)
  {
    // PATCH: solo se valida lo que el cliente envía.
    if (dto.CategoryId is int categoryId)
      await EnsureCategoryExistsAsync(categoryId, ct);

    if (!string.IsNullOrWhiteSpace(dto.SKU))
      await EnsureSkuIsFreeAsync(dto.SKU, excludeId: id, ct);
  }

  // ---- helpers privados ---------------------------------------------------

  private async Task EnsureCategoryExistsAsync(int categoryId, CancellationToken ct)
  {
    // 400 y no 404: el recurso pedido es el producto; la categoría inexistente
    // hace que el request sea inválido en sí mismo.
    if (!await categoryRepository.ExistsAsync(categoryId, ct))
      throw new BadOperationAppException($"Category with id {categoryId} does not exist.");
  }

  private async Task EnsureSkuIsFreeAsync(string sku, int? excludeId, CancellationToken ct)
  {
    if (await productRepository.SkuExistsAsync(sku, excludeId, ct))
      throw new ConflictAppException($"SKU '{sku}' is already registered.");
  }
}
