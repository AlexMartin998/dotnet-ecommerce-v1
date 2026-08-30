using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Service.Crud;
using ApiEcommerce.Shared.Storage;

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
  /// Reemplaza la imagen del producto y devuelve el producto ya actualizado.
  /// Borra la imagen anterior si era un archivo gestionado por la API.
  /// </summary>
  /// <remarks>
  /// Es un endpoint aparte y no un campo del PATCH: subir un binario es
  /// <c>multipart/form-data</c>, y meterlo en el mismo endpoint que el JSON obliga a
  /// cambiar el content-type de <b>todos</b> los clientes existentes. El código de
  /// referencia hizo exactamente eso y rompió su propio PUT.
  /// </remarks>
  Task<ProductDto> SetImageAsync(int id, FileUpload upload, CancellationToken ct = default);

  /// <summary>
  /// Descuenta stock por SKU y devuelve el producto ya actualizado.
  /// 404 si el SKU no existe; 409 si el stock es insuficiente.
  /// </summary>
  Task<ProductDto> BuyAsync(BuyProductDto dto, CancellationToken ct = default);

}
