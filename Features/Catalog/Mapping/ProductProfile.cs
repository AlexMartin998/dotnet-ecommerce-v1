using AutoMapper;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Mapping;


/// <summary>Mapeos entre <c>Product</c> y sus DTOs.</summary>
public class ProductProfile : Profile
{

  /// <summary>Registra los mapeos de lectura, creación y PATCH.</summary>
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
    // Se recortan al escribir Name y SKU: sin normalizar, " SKU-1" y "SKU-1" conviven pese
    // al indice unico, y obliga a envolver la columna en TRIM() al comparar, anulandolo.
    CreateMap<CreateProductDto, Product>()
        .ForMember(d => d.Name, o => o.MapFrom(s => s.Name.Trim()))
        .ForMember(d => d.SKU, o => o.MapFrom(s => s.SKU.Trim()))
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.RowVersion, o => o.Ignore())   // lo gestiona SQL Server
        .ForMember(d => d.Category, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore());

    // PATCH parcial sobre la entidad rastreada: omitir el campo o enviarlo null significa
    // "no tocar", y para vaciar Description/ImageUrl hay que enviar "".
    //
    // `s.X ?? d.X` en vez de ForAllMembers(Condition(...)): la Condition recibe el valor
    // ya convertido al destino, así que un `int?` nulo llega como 0 y machaca el campo.
    CreateMap<UpdateProductDto, Product>()
        .ForMember(d => d.Id, o => o.Ignore())
        .ForMember(d => d.RowVersion, o => o.Ignore())   // lo gestiona SQL Server
        .ForMember(d => d.Category, o => o.Ignore())
        .ForMember(d => d.CreatedAt, o => o.Ignore())
        .ForMember(d => d.UpdatedAt, o => o.Ignore())
        .ForMember(d => d.Name, o => o.MapFrom((s, d) => s.Name != null ? s.Name.Trim() : d.Name))
        .ForMember(d => d.Description, o => o.MapFrom((s, d) => s.Description ?? d.Description))
        .ForMember(d => d.Price, o => o.MapFrom((s, d) => s.Price ?? d.Price))
        .ForMember(d => d.ImageUrl, o => o.MapFrom((s, d) => s.ImageUrl ?? d.ImageUrl))
        .ForMember(d => d.SKU, o => o.MapFrom((s, d) => s.SKU != null ? s.SKU.Trim() : d.SKU))
        .ForMember(d => d.Stock, o => o.MapFrom((s, d) => s.Stock ?? d.Stock))
        .ForMember(d => d.CategoryId, o => o.MapFrom((s, d) => s.CategoryId ?? d.CategoryId));
    // El If-Match del cliente no se mapea a la entidad: solo sirve para comparar.
  }

}
