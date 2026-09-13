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
    ICategoryRepository categoryRepository,
    IProductVariantRepository variantRepository)
  : IEntityRules<Product, CreateProductDto, UpdateProductDto>
{
  public string EntityName => "Product";

  public async Task EnsureCanCreateAsync(CreateProductDto dto, CancellationToken ct = default)
  {
    await EnsureCategoryExistsAsync(dto.CategoryId, ct);
    await EnsureSkuIsFreeAsync(dto.SKU, excludeId: null, ct);
    await EnsureSlugIsFreeAsync(dto, ct);
    await EnsureVariantsAreValidAsync(dto, ct);
  }

  public async Task EnsureCanUpdateAsync(
      int id, UpdateProductDto dto, Product existing, CancellationToken ct = default)
  {
    EntityTags.EnsureMatches(dto.IfMatch, existing.RowVersion);

    // PATCH: solo se valida lo que el cliente envía.
    if (dto.CategoryId is int categoryId)
      await EnsureCategoryExistsAsync(categoryId, ct);

    if (!string.IsNullOrWhiteSpace(dto.SKU))
    {
      await EnsureSkuIsFreeAsync(dto.SKU, excludeId: id, ct);

      // También contra las variantes de OTROS productos: en uno sin tallas este SKU pasa a
      // ser el de su variante (ProductService lo sincroniza), y la clave de carrito es única.
      if (await variantRepository.SkuExistsAsync(dto.SKU, excludeProductId: id, ct))
        throw new ConflictAppException($"SKU '{dto.SKU.Trim()}' is already registered.");
    }
  }

  // ---- helpers privados ---------------------------------------------------

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

  /// <summary>
  /// El slug es único, y cuando se deriva del nombre dos productos que se llaman igual
  /// chocan. El mensaje dice qué hacer, porque el cliente no eligió ese slug.
  /// </summary>
  /// <remarks>
  /// La garantía sigue siendo el índice único de la base; esto solo da un 409 con un
  /// mensaje útil en el caso normal. Se deriva con el mismo helper que el mapeador.
  /// </remarks>
  private async Task EnsureSlugIsFreeAsync(CreateProductDto dto, CancellationToken ct)
  {
    var slug = Slugs.From(dto.Slug) ?? Slugs.From(dto.Name);

    if (slug is null || !await productRepository.SlugExistsAsync(slug, ct)) return;

    throw new ConflictAppException(
        dto.Slug is null
            ? $"The slug '{slug}', derived from the product name, is already in use. " +
              "Send an explicit 'slug' to choose a different one."
            : $"Slug '{slug}' is already in use.");
  }

  /// <summary>
  /// Las tallas del alta: o <c>Stock</c> o <c>Variants</c>, tallas sin repetir y SKU libres.
  /// </summary>
  /// <remarks>
  /// Se calculan los SKU igual que <c>ProductMapper</c>, con <see cref="VariantSkus"/>: si la
  /// regla y el mapeador los derivaran por separado, la comprobación miraría otro SKU.
  /// </remarks>
  private async Task EnsureVariantsAreValidAsync(CreateProductDto dto, CancellationToken ct)
  {
    if (dto.Variants is not { Count: > 0 } variants)
    {
      // Sin tallas, la única variante nace con el SKU del producto, y ese SKU también tiene
      // que estar libre en la tabla de variantes (un producto retirado conserva los suyos).
      await EnsureVariantSkuIsFreeAsync(dto.SKU.Trim(), ct);
      return;
    }

    // 400: son dos stocks distintos para lo mismo, y no hay forma buena de elegir uno.
    if (dto.Stock is not null)
      throw new BadOperationAppException(
          "Send either 'stock' for a product without sizes or 'variants', not both.");

    var sizes = variants.Select(v => v.Size.Trim()).ToList();

    if (sizes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sizes.Count)
      throw new BadOperationAppException("A size can't appear twice in 'variants'.");

    var skus = variants
        .Select(v => string.IsNullOrWhiteSpace(v.SKU) ? VariantSkus.For(dto.SKU, v.Size) : v.SKU.Trim())
        .ToList();

    if (skus.Distinct(StringComparer.OrdinalIgnoreCase).Count() != skus.Count)
      throw new BadOperationAppException("Two variants can't share the same SKU.");

    foreach (var sku in skus)
    {
      if (sku.Length > 50)
        throw new BadOperationAppException(
            $"The variant SKU '{sku}' is longer than 50 characters. Send an explicit 'sku' for it.");

      await EnsureVariantSkuIsFreeAsync(sku, ct);
    }
  }

  private async Task EnsureVariantSkuIsFreeAsync(string sku, CancellationToken ct)
  {
    if (await variantRepository.SkuExistsAsync(sku, ct: ct))
      throw new ConflictAppException($"SKU '{sku}' is already registered.");
  }
}
