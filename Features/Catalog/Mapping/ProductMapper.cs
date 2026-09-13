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
  /// <remarks>
  /// Lo generado se completa a mano con las tallas: <c>Stock</c> y <c>Sizes</c> ya no son
  /// columnas, se derivan de las variantes activas, y eso no cabe en una proyección.
  /// </remarks>
  public ProductDto ToDto(Product entity)
  {
    ArgumentNullException.ThrowIfNull(entity);

    var dto = Project(entity);

    // Solo las activas: una talla desactivada no se vende, así que no se enseña ni suma.
    var active = entity.Variants
        .Where(v => v.IsActive)
        .OrderBy(v => v.Position)
        .ThenBy(v => v.Id)
        .ToList();

    dto.Variants = [.. active.Select(ToVariantDto)];
    // En long y saturando: dos tallas grandes desbordaban int y cada listado con ese
    // producto daba 500 (OverflowException).
    dto.Stock = (int)Math.Min(active.Sum(v => (long)v.Stock), int.MaxValue);
    dto.Sizes = [.. active.Where(v => v.Size is not null).Select(v => v.Size!)];

    return dto;
  }

  /// <summary>Una variante, tal y como la ven la ficha y el panel.</summary>
  public static ProductVariantDto ToVariantDto(ProductVariant variant)
  {
    ArgumentNullException.ThrowIfNull(variant);

    return new ProductVariantDto
    {
      Id = variant.Id,
      SKU = variant.SKU,
      Size = variant.Size,
      Stock = variant.Stock,
      Available = variant.IsActive && variant.Stock > 0,
      Position = variant.Position,
      IsActive = variant.IsActive,
      RowVersion = ToBase64(variant.RowVersion)
    };
  }

  // CategoryName viaja plano porque la navegación puede venir sin cargar.
  [MapProperty("Category.Name", nameof(ProductDto.CategoryName))]
  [MapProperty("Category.Slug", nameof(ProductDto.CategorySlug))]
  [MapperIgnoreSource(nameof(Product.DeletedAt))]
  [MapperIgnoreSource(nameof(Product.Variants))]
  [MapperIgnoreTarget(nameof(ProductDto.Variants))]
  [MapperIgnoreTarget(nameof(ProductDto.Stock))]
  [MapperIgnoreTarget(nameof(ProductDto.Sizes))]
  private partial ProductDto Project(Product entity);

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

    entity.Variants = BuildVariants(dto, entity.SKU);

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
    // El SKU del producto puede cambiar; el de sus variantes no (vive en carritos y órdenes).
    entity.SKU = dto.SKU?.Trim() ?? entity.SKU;
    entity.CategoryId = dto.CategoryId ?? entity.CategoryId;

    // Las colecciones se REEMPLAZAN enteras, no se fusionan: sin eso no habría forma de
    // quitar una etiqueta. Omitirlas sigue significando "no tocar".
    if (dto.Tags is not null) entity.Tags = [.. dto.Tags];

    // Stock y tallas ya no se tocan aquí: se editan en /product/{id}/variants (planning/27).

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
  // Las variantes las construye BuildVariants: o las tallas del DTO, o la única sin talla.
  [MapperIgnoreTarget(nameof(Product.Variants))]
  [MapperIgnoreSource(nameof(CreateProductDto.Variants))]
  [MapperIgnoreSource(nameof(CreateProductDto.Stock))]
  // Lo pone ToEntity a mano: derivarlo del nombre no cabe en una proyección, y el del DTO
  // es opcional. Los dos lados se declaran, o el generador avisa de cada uno.
  [MapperIgnoreTarget(nameof(Product.Slug))]
  [MapperIgnoreSource(nameof(CreateProductDto.Slug))]
  private partial Product Build(CreateProductDto dto);

  /// <summary>Las variantes del alta: una por talla, o la única sin talla con el SKU del producto.</summary>
  /// <remarks>
  /// El SKU de cada talla sale de <see cref="VariantSkus"/>, el mismo helper con el que
  /// <c>ProductRules</c> comprueba que está libre.
  /// </remarks>
  private static List<ProductVariant> BuildVariants(CreateProductDto dto, string productSku)
  {
    if (dto.Variants is not { Count: > 0 } variants)
      return [new ProductVariant { SKU = productSku, Size = null, Stock = dto.Stock ?? 0, Position = 0 }];

    return [.. variants.Select((v, index) => new ProductVariant
    {
      Size = v.Size.Trim(),
      // Vacío cuenta como «no viene»: `[RegularExpression]` da por buena la cadena vacía.
      SKU = string.IsNullOrWhiteSpace(v.SKU) ? VariantSkus.For(productSku, v.Size) : v.SKU.Trim(),
      Stock = v.Stock,
      Position = v.Position ?? index
    })];
  }

  // El rowversion es binario en la base y texto en una cabecera HTTP.
  private static string? ToBase64(byte[]? rowVersion)
      => rowVersion is null ? null : Convert.ToBase64String(rowVersion);

  // Las imágenes salen ordenadas: la 0 es la del listado, y el orden es parte del dato.
  private static IReadOnlyList<string> ToUrls(ICollection<ProductImage> images)
      // Desempate por Id: dos imágenes con la misma posición no pueden cambiar de orden entre
      // peticiones, y el front enseña la segunda al pasar el ratón.
      => [.. images.OrderBy(i => i.Position).ThenBy(i => i.Id).Select(i => i.Url)];

}
