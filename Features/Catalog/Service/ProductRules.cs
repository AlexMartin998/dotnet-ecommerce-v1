using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Reglas de negocio de <see cref="Product"/>: SKU único, categoría existente y
/// comprobación de <c>If-Match</c>.
/// </summary>
/// <remarks>
/// La FK se valida aquí porque, si se deja pasar un <c>CategoryId</c> inexistente, EF
/// falla con un error de clave foránea y el cliente recibe un 500 en vez de un 400.
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
    EnsureVersionMatches(dto.IfMatch, existing);

    // PATCH: solo se valida lo que el cliente envía.
    if (dto.CategoryId is int categoryId)
      await EnsureCategoryExistsAsync(categoryId, ct);

    if (!string.IsNullOrWhiteSpace(dto.SKU))
      await EnsureSkuIsFreeAsync(dto.SKU, excludeId: id, ct);
  }

  // ---- helpers privados ---------------------------------------------------

  /// <summary>
  /// Compara el <c>If-Match</c> del cliente con la versión en base y cierra el
  /// lost update entre dos administradores.
  /// </summary>
  /// <remarks>
  /// Es opcional: sin <c>If-Match</c> el PATCH funciona como siempre. La ventana entre
  /// esta comprobación y el UPDATE la cubre el <c>[Timestamp]</c> de la entidad.
  /// </remarks>
  private static void EnsureVersionMatches(IReadOnlyList<string>? clientVersions, Product existing)
  {
    if (clientVersions is null || clientVersions.Count == 0) return;

    var expected = new List<byte[]>(clientVersions.Count);

    foreach (var version in clientVersions)
    {
      try
      {
        expected.Add(Convert.FromBase64String(version));
      }
      catch (FormatException)
      {
        // 400 y no 412: no es que la precondición falle, es que ni siquiera es un token.
        throw new BadOperationAppException("The If-Match header is not a valid entity tag.");
      }
    }

    // El RFC 9110 dice que basta con que UNA case.
    if (existing.RowVersion is not null && expected.Any(e => e.SequenceEqual(existing.RowVersion)))
      return;

    throw new PreconditionFailedAppException();
  }

  private async Task EnsureCategoryExistsAsync(int categoryId, CancellationToken ct)
  {
    // 400 y no 404: el recurso pedido es el producto, no la categoría.
    if (!await categoryRepository.ExistsAsync(categoryId, ct))
      throw new BadOperationAppException($"Category with id {categoryId} does not exist.");
  }

  private async Task EnsureSkuIsFreeAsync(string sku, int? excludeId, CancellationToken ct)
  {
    if (await productRepository.SkuExistsAsync(sku, excludeId, ct))
      throw new ConflictAppException($"SKU '{sku}' is already registered.");
  }
}
