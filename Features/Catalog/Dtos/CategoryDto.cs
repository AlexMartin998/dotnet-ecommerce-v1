namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Categoría tal y como la ve el cliente.</summary>
public class CategoryDto
{

  public int Id { get; set; }
  public string Name { get; set; } = string.Empty;

  /// <summary>Identificador de la URL pública: <c>/category/slug/{slug}</c>.</summary>
  public string Slug { get; set; } = string.Empty;
  public string? Description { get; set; }

}
