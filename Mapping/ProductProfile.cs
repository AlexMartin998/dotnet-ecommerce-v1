using AutoMapper;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;

namespace ApiEcommerce.Mapping;


public class ProductProfile: Profile
{

  public ProductProfile()
  {
    CreateMap<Product, ProductDto>().ReverseMap();
    CreateMap<Product, CreateProductDto>().ReverseMap();
    CreateMap<Product, UpdateProductDto>().ReverseMap();
  }

}
