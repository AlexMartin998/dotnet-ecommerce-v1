using AutoMapper;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;

namespace ApiEcommerce.Mapping;


public class ProductProfile : Profile
{

  public ProductProfile()
  {
    // lectura: CategoryName viaja plano (la navegación puede venir sin cargar)
    CreateMap<Product, ProductDto>()
        .ForMember(d => d.CategoryName,
                   o => o.MapFrom(s => s.Category != null ? s.Category.Name : null));

    // escritura: la navegación Category se ignora, se trabaja solo con CategoryId
    CreateMap<CreateProductDto, Product>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.Category, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore());

    // PATCH parcial, mapeado SOBRE la entidad rastreada.
    //
    // `s.X ?? d.X` en vez de `.ForAllMembers(o => o.Condition(...))`: la Condition
    // recibe el valor YA convertido al tipo del destino, así que un `int?` nulo
    // llegaba como 0 y el PATCH machacaba CategoryId/Stock/Price con ceros
    // (CategoryId = 0 reventaba la FK y salía un 500). Esto es explícito y no
    // depende de la semántica interna de AutoMapper.
    //
    // Semántica: omitir el campo (o enviarlo null) significa "no tocar".
    // Para vaciar Description/ImageUrl hay que enviar "", no null.
    CreateMap<UpdateProductDto, Product>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.Category, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore())
        .ForMember(d => d.Name, o => o.MapFrom((s, d) => s.Name ?? d.Name))
        .ForMember(d => d.Description, o => o.MapFrom((s, d) => s.Description ?? d.Description))
        .ForMember(d => d.Price, o => o.MapFrom((s, d) => s.Price ?? d.Price))
        .ForMember(d => d.ImageUrl, o => o.MapFrom((s, d) => s.ImageUrl ?? d.ImageUrl))
        .ForMember(d => d.SKU, o => o.MapFrom((s, d) => s.SKU ?? d.SKU))
        .ForMember(d => d.Stock, o => o.MapFrom((s, d) => s.Stock ?? d.Stock))
        .ForMember(d => d.CategoryId, o => o.MapFrom((s, d) => s.CategoryId ?? d.CategoryId));
  }

}
