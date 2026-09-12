using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Shared.Crud;
using Riok.Mapperly.Abstractions;

namespace ApiEcommerce.Features.Catalog.Mapping;


/// <summary>Mapeos entre <c>Product</c> y sus DTOs.</summary>
[Mapper]
public partial class ProductMapper
    : IEntityMapper<Product, ProductDto, CreateProductDto, UpdateProductDto>
{

  /// <inheritdoc />
  // CategoryName viaja plano porque la navegación puede venir sin cargar.
  [MapProperty("Category.Name", nameof(ProductDto.CategoryName))]
  public partial ProductDto ToDto(Product entity);

  /// <inheritdoc />
  public Product ToEntity(CreateProductDto dto)
  {
    var entity = Build(dto);
    entity.Name = entity.Name.Trim();
    entity.SKU = entity.SKU.Trim();

    return entity;
  }

  /// <inheritdoc />
  /// <remarks>
  /// Campo a campo y no por convención: un mapeador que decide por el valor ya convertido
  /// al destino recibe un <c>int?</c> nulo como 0, y un CategoryId en 0 rompe la FK.
  /// </remarks>
  public void Apply(UpdateProductDto dto, Product entity)
  {
    ArgumentNullException.ThrowIfNull(dto);
    ArgumentNullException.ThrowIfNull(entity);

    entity.Name = dto.Name?.Trim() ?? entity.Name;
    entity.Description = dto.Description ?? entity.Description;
    entity.Price = dto.Price ?? entity.Price;
    entity.ImageUrl = dto.ImageUrl ?? entity.ImageUrl;
    entity.SKU = dto.SKU?.Trim() ?? entity.SKU;
    entity.Stock = dto.Stock ?? entity.Stock;
    entity.CategoryId = dto.CategoryId ?? entity.CategoryId;

    // IfMatch no se mapea: solo sirve para comparar el ETag.
  }

  // Se recortan Name y SKU: sin normalizar, " SKU-1" y "SKU-1" conviven pese al índice
  // único.
  [MapperIgnoreTarget(nameof(Product.Id))]
  [MapperIgnoreTarget(nameof(Product.RowVersion))]
  [MapperIgnoreTarget(nameof(Product.Category))]
  [MapperIgnoreTarget(nameof(Product.CreatedAt))]
  [MapperIgnoreTarget(nameof(Product.UpdatedAt))]
  private partial Product Build(CreateProductDto dto);

  // El rowversion es binario en la base y texto en una cabecera HTTP.
  private static string? ToBase64(byte[]? rowVersion)
      => rowVersion is null ? null : Convert.ToBase64String(rowVersion);

}
