using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using AutoMapper;

namespace ApiEcommerce.Mapping;


public class MappingProfile : Profile
{
  public MappingProfile()
  {
    CreateMap<Category, CategoryDto>().ReverseMap();
    CreateMap<CreateCategoryDto, Category>();
  }
}
