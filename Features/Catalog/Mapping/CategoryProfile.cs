using AutoMapper;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Mapping;


/// <summary>Mapeos entre <c>Category</c> y sus DTOs.</summary>
public class CategoryProfile : Profile
{

  /// <summary>Registra los mapeos de lectura, creación y PATCH.</summary>
  public CategoryProfile()
  {
    // lectura
    CreateMap<Category, CategoryDto>();

    // escritura: los campos de auditoría los estampa AppDbContext, no el mapper
    CreateMap<CreateCategoryDto, Category>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore());

    // PATCH parcial: `s.X ?? d.X` = "lo que no venga, no se toca". Ver ProductProfile
    // sobre por qué no se usa ForAllMembers(Condition(...)).
    CreateMap<UpdateCategoryDto, Category>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore())
        .ForMember(d => d.Name, o => o.MapFrom((s, d) => s.Name ?? d.Name))
        .ForMember(d => d.Description, o => o.MapFrom((s, d) => s.Description ?? d.Description));
  }

}
