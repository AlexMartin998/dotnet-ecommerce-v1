using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Service;
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
  [MapProperty("Category.Slug", nameof(ProductDto.CategorySlug))]
  [MapperIgnoreSource(nameof(Product.DeletedAt))]
  public partial ProductDto ToDto(Product entity);

  /// <inheritdoc />
  public Product ToEntity(CreateProductDto dto)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var entity = Build(dto);
    entity.Name = entity.Name.Trim();
    entity.SKU = entity.SKU.Trim();

    // El slug que manda el cliente gana; si no viene, sale del nombre. Que quede vacío es
    // imposible: el nombre es obligatorio y no puede ser solo separadores... salvo que lo
    // sea, y entonces el SKU es el último recurso antes de una fila sin URL.
    entity.Slug = Slugs.From(dto.Slug) ?? Slugs.From(entity.Name) ?? entity.SKU.ToLowerInvariant();

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
    entity.SKU = dto.SKU?.Trim() ?? entity.SKU;
    entity.Stock = dto.Stock ?? entity.Stock;
    entity.CategoryId = dto.CategoryId ?? entity.CategoryId;

    // Las colecciones se REEMPLAZAN enteras, no se fusionan: sin eso no habría forma de
    // quitar una etiqueta. Omitirlas sigue significando "no tocar".
    if (dto.Tags is not null) entity.Tags = [.. dto.Tags];
    if (dto.Sizes is not null) entity.Sizes = [.. dto.Sizes];

    // El slug NO se actualiza: cambiarlo rompería los enlaces que ya circulan.
    // IfMatch tampoco se mapea: solo sirve para comparar el ETag.
  }

  // Se recortan Name y SKU: sin normalizar, " SKU-1" y "SKU-1" conviven pese al índice
  // único.
  [MapperIgnoreTarget(nameof(Product.Id))]
  [MapperIgnoreTarget(nameof(Product.RowVersion))]
  [MapperIgnoreTarget(nameof(Product.Category))]
  [MapperIgnoreTarget(nameof(Product.CreatedAt))]
  [MapperIgnoreTarget(nameof(Product.UpdatedAt))]
  [MapperIgnoreTarget(nameof(Product.DeletedAt))]
  [MapperIgnoreTarget(nameof(Product.Images))]
  // Lo pone ToEntity a mano: derivarlo del nombre no cabe en una proyección, y el del DTO
  // es opcional. Los dos lados se declaran, o el generador avisa de cada uno.
  [MapperIgnoreTarget(nameof(Product.Slug))]
  [MapperIgnoreSource(nameof(CreateProductDto.Slug))]
  private partial Product Build(CreateProductDto dto);

  // El rowversion es binario en la base y texto en una cabecera HTTP.
  private static string? ToBase64(byte[]? rowVersion)
      => rowVersion is null ? null : Convert.ToBase64String(rowVersion);

  // Las imágenes salen ordenadas: la 0 es la del listado, y el orden es parte del dato.
  private static IReadOnlyList<string> ToUrls(ICollection<ProductImage> images)
      => [.. images.OrderBy(i => i.Position).Select(i => i.Url)];

}
