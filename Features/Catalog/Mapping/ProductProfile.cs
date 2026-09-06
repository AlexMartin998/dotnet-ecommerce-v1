using AutoMapper;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Mapping;


public class ProductProfile : Profile
{

  public ProductProfile()
  {
    // lectura: CategoryName viaja plano (la navegación puede venir sin cargar)
    CreateMap<Product, ProductDto>()
        .ForMember(d => d.CategoryName,
                   o => o.MapFrom(s => s.Category != null ? s.Category.Name : null))
        // El rowversion es binario en la base y texto en una cabecera HTTP.
        .ForMember(d => d.RowVersion,
                   o => o.MapFrom(s => s.RowVersion != null ? Convert.ToBase64String(s.RowVersion) : null));

    // escritura: la navegación Category se ignora, se trabaja solo con CategoryId
    CreateMap<CreateProductDto, Product>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.RowVersion, o => o.Ignore())   // lo gestiona SQL Server
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
        .ForMember(d => d.RowVersion, o => o.Ignore())   // lo gestiona SQL Server
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
    // Nota: UpdateProductDto.RowVersion NO se mapea a la entidad. Lo gestiona SQL Server;
    // el valor que manda el cliente sirve solo para COMPARAR (ver ProductRules).
  }

}
