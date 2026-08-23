using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Service.Crud;

namespace ApiEcommerce.Service;


/// <summary>
/// Servicio de productos: CRUD heredado del genérico + las operaciones propias
/// del dominio (búsqueda, listado por categoría, compra).
/// </summary>
public interface IProductService
  : ICrudService<ProductDto, CreateProductDto, UpdateProductDto>
{

  /// <summary>Productos de una categoría. Si la categoría no existe, lanza <c>NotFoundAppException</c>.</summary>
  Task<IEnumerable<ProductDto>> GetForCategoryAsync(int categoryId, CancellationToken ct = default);

  /// <summary>Búsqueda por coincidencia parcial de nombre. Sin resultados devuelve lista vacía, no 404.</summary>
  Task<IEnumerable<ProductDto>> SearchAsync(string name, CancellationToken ct = default);

  /// <summary>
  /// Descuenta stock por SKU y devuelve el producto ya actualizado.
  /// 404 si el SKU no existe; 409 si el stock es insuficiente.
  /// </summary>
  Task<ProductDto> BuyAsync(BuyProductDto dto, CancellationToken ct = default);

}
