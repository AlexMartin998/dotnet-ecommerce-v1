using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Shared.Mapping;


// LEGACY — se conserva comentado como registro de aprendizaje.
//
// Este era el "ModelMapper" único al estilo Spring Boot: un solo Profile con todos
// los mapeos. La convención del proyecto pasó a ser un Profile por entidad
// (CategoryProfile, ProductProfile), así que estos mapas quedaban DUPLICADOS.
// Mantenerlos activos hacía que dos Profile declararan el mismo CreateMap y que
// no quedara claro cuál manda.
//
// public class MappingProfile : Profile
// {
//   public MappingProfile()
//   {
//     CreateMap<Category, CategoryDto>().ReverseMap();
//     CreateMap<CreateCategoryDto, Category>();
//   }
// }
