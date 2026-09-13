using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Service;
using ApiEcommerce.Shared.Crud;
using Riok.Mapperly.Abstractions;

namespace ApiEcommerce.Features.Catalog.Mapping;


/// <summary>Mapeos entre <c>Category</c> y sus DTOs.</summary>
[Mapper]
public partial class CategoryMapper
    : IEntityMapper<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>
{

  /// <inheritdoc />
  // CategoryDto no expone la auditoría, a diferencia de ProductDto.
  [MapperIgnoreSource(nameof(Category.CreatedAt))]
  [MapperIgnoreSource(nameof(Category.UpdatedAt))]
  public partial CategoryDto ToDto(Category entity);

  /// <inheritdoc />
  public Category ToEntity(CreateCategoryDto dto)
  {
    var entity = Build(dto);
    entity.Name = entity.Name.Trim();
    // El DTO solo admite letras, dígitos y espacios, así que el slug nunca sale vacío.
    entity.Slug = Slugs.From(entity.Name) ?? string.Empty;

    return entity;
  }

  /// <inheritdoc />
  public void Apply(UpdateCategoryDto dto, Category entity)
  {
    ArgumentNullException.ThrowIfNull(dto);
    ArgumentNullException.ThrowIfNull(entity);

    // El slug NO se actualiza: cambiarlo rompería los enlaces que ya circulan.
    entity.Name = dto.Name?.Trim() ?? entity.Name;
    entity.Description = dto.Description ?? entity.Description;
  }

  // Se recorta al escribir: sin normalizar, " Bebidas" y "Bebidas" conviven pese al
  // índice único.
  [MapperIgnoreTarget(nameof(Category.Id))]
  [MapperIgnoreTarget(nameof(Category.CreatedAt))]
  [MapperIgnoreTarget(nameof(Category.UpdatedAt))]
  [MapperIgnoreTarget(nameof(Category.Slug))]
  // Destacar no es parte del alta: va por PUT /category/featured, que vigila el límite.
  [MapperIgnoreTarget(nameof(Category.FeaturedPosition))]
  private partial Category Build(CreateCategoryDto dto);

}
