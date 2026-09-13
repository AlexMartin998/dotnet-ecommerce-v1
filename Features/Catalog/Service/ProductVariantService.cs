using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Shared.Db;

namespace ApiEcommerce.Features.Catalog.Service;


/// <inheritdoc cref="IProductVariantService"/>
/// <remarks>
/// No compone <c>CrudService</c>: una variante no se borra ni se actualiza entera, y sus
/// reglas miran a las hermanas del mismo producto, que el CRUD genérico no conoce.
/// </remarks>
public sealed class ProductVariantService(
    IProductRepository products,
    IProductVariantRepository variants,
    ITransactionRunner transactions) : IProductVariantService
{
  public async Task<IReadOnlyList<ProductVariantDto>> GetForProductAsync(
      int productId, CancellationToken ct = default)
  {
    await EnsureProductExistsAsync(productId, ct);

    return [.. (await variants.GetForProductAsync(productId, ct)).Select(ProductMapper.ToVariantDto)];
  }

  public Task<ProductVariantDto> AddAsync(
      int productId, CreateProductVariantDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    // Transacción + lock del producto: las reglas miran a las hermanas y, sin serializar, dos
    // administradores leen el mismo estado previo y los dos escriben (ver LockProductAsync).
    return transactions.ExecuteAsync(async token =>
    {
      var product = await products.GetByIdAsync(productId, token)
          ?? throw new NotFoundAppException("Product", productId);

      await variants.LockProductAsync(productId, token);

      var siblings = await variants.GetForProductAsync(productId, token);
      var size = dto.Size.Trim();

      if (siblings.Count >= VariantLimits.MaxVariants)
        throw new ConflictAppException($"A product can't have more than {VariantLimits.MaxVariants} sizes.");

      // Contra TODAS, también las desactivadas: el índice único (ProductId, Size) no distingue,
      // y reactivar la vieja es la forma de recuperar esa talla.
      if (siblings.Any(v => string.Equals(v.Size, size, StringComparison.OrdinalIgnoreCase)))
        throw CatalogErrors.DuplicateSize(size);

      if (siblings.Any(v => v.IsActive && v.Size is null))
        throw CatalogErrors.VariantKindMismatch();

      // Vacío cuenta como «no viene»: `[RegularExpression]` da por buena la cadena vacía.
      var sku = string.IsNullOrWhiteSpace(dto.SKU) ? VariantSkus.For(product.SKU, size) : dto.SKU.Trim();

      if (sku.Length > 50)
        throw new BadOperationAppException(
            $"The variant SKU '{sku}' is longer than 50 characters. Send an explicit 'sku'.");

      // Da el 409 con mensaje; la garantía es el índice único, que el handler traduce igual.
      if (await variants.SkuExistsAsync(sku, ct: token))
        throw new ConflictAppException($"SKU '{sku}' is already registered.");

      var variant = await variants.AddAsync(new ProductVariant
      {
        ProductId = productId,
        Size = size,
        SKU = sku,
        Stock = dto.Stock,
        Position = dto.Position ?? (siblings.Count == 0 ? 0 : siblings.Max(v => v.Position) + 1)
      }, token);

      return ProductMapper.ToVariantDto(variant);
    }, ct);
  }

  public Task<ProductVariantDto> UpdateAsync(
      int productId, int variantId, UpdateProductVariantDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    return transactions.ExecuteAsync(async token =>
    {
      // El lock ANTES de leer: si no, la lectura de abajo podría ser la del estado previo.
      await variants.LockProductAsync(productId, token);

      var variant = await variants.GetTrackedAsync(productId, variantId, token)
          ?? throw new NotFoundAppException("ProductVariant", variantId);

      // Una compra también cambia el rowversion: reponer con lo leído antes de una venta da
      // 412 en vez de pisar el descuento.
      EntityTags.EnsureMatches(dto.IfMatch, variant.RowVersion);

      if (dto.IsActive is true && !variant.IsActive)
        await EnsureCanActivateAsync(variant, token);

      variant.Stock = dto.Stock ?? variant.Stock;
      variant.Position = dto.Position ?? variant.Position;
      variant.IsActive = dto.IsActive ?? variant.IsActive;

      // Con rastreo y SaveChanges, no ExecuteUpdate: así EF mete el RowVersion en el WHERE y
      // la ventana entre la comparación de arriba y el UPDATE da 409 concurrency_conflict.
      return ProductMapper.ToVariantDto(await variants.UpdateAsync(variant, token));
    }, ct);
  }

  /// <summary>Reactivar no puede dejar una variante sin talla al lado de tallas activas.</summary>
  private async Task EnsureCanActivateAsync(ProductVariant variant, CancellationToken ct)
  {
    var others = (await variants.GetForProductAsync(variant.ProductId, ct))
        .Where(v => v.Id != variant.Id && v.IsActive);

    var mixes = variant.Size is null
        ? others.Any()
        : others.Any(v => v.Size is null);

    if (mixes) throw CatalogErrors.VariantKindMismatch();
  }

  private async Task EnsureProductExistsAsync(int productId, CancellationToken ct)
  {
    if (!await products.ExistsAsync(productId, ct))
      throw new NotFoundAppException("Product", productId);
  }
}
