using ApiEcommerce.Features.Catalog.Dtos;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>Administración de las tallas de un producto.</summary>
/// <remarks>
/// No hay borrado: una talla vendida está citada por órdenes, así que se desactiva. Ni se
/// cambia su talla o su SKU: viven en carritos y comprobantes.
/// </remarks>
public interface IProductVariantService
{
  /// <summary>Todas las variantes, activas o no, por posición. 404 si el producto no existe.</summary>
  Task<IReadOnlyList<ProductVariantDto>> GetForProductAsync(int productId, CancellationToken ct = default);

  /// <summary>Añade una talla. 404 sin producto; 409 <c>duplicate_size</c>, <c>variant_kind_mismatch</c> o SKU ocupado.</summary>
  Task<ProductVariantDto> AddAsync(int productId, CreateProductVariantDto dto, CancellationToken ct = default);

  /// <summary>Repone stock, reordena o (des)activa. 404, 409 <c>variant_kind_mismatch</c>, 412 con <c>If-Match</c> viejo.</summary>
  Task<ProductVariantDto> UpdateAsync(
      int productId, int variantId, UpdateProductVariantDto dto, CancellationToken ct = default);
}
