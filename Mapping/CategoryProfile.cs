using AutoMapper;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;

namespace ApiEcommerce.Mapping;


public class CategoryProfile : Profile
{

  public CategoryProfile()
  {
    // lectura
    CreateMap<Category, CategoryDto>();

    // escritura: los campos de auditoría los estampa AppDbContext, no el mapper
    CreateMap<CreateCategoryDto, Category>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore());

    // PATCH parcial: `s.X ?? d.X` = "lo que no venga, no se toca".
    // Ver el comentario largo de ProductProfile sobre por qué no se usa
    // ForAllMembers(Condition(...)).
    CreateMap<UpdateCategoryDto, Category>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore())
        .ForMember(d => d.Name, o => o.MapFrom((s, d) => s.Name ?? d.Name))
        .ForMember(d => d.Description, o => o.MapFrom((s, d) => s.Description ?? d.Description));
  }

}
