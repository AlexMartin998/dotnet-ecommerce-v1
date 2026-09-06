using ApiEcommerce.Shared.Storage;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Servicio de productos: el CRUD del contrato genérico más las operaciones propias
/// del dominio (búsqueda, listado por categoría, imagen y compra).
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
  /// Es una operación aparte y no un campo del PATCH: subir un binario exige
  /// <c>multipart/form-data</c> y cambiaría el content-type de todos los clientes.
  /// </remarks>
  Task<ProductDto> SetImageAsync(int id, FileUpload upload, CancellationToken ct = default);

  /// <summary>
  /// Descuenta stock por SKU y devuelve el producto ya actualizado.
  /// 404 si el SKU no existe; 409 si el stock es insuficiente; 422 si la intención se
  /// reusó con otra compra distinta.
  /// </summary>
  /// <remarks>
  /// <paramref name="intent"/> no tiene valor por defecto a propósito: la idempotencia es
  /// una invariante de la operación, no del atributo del controller, así que llamarla
  /// desde otro sitio obliga a decidir. Renunciar se escribe <see cref="CommandIntent.None"/>.
  /// </remarks>
  /// <param name="dto">Qué se compra y cuánto.</param>
  /// <param name="intent">La identidad de este intento de compra.</param>
  /// <param name="buyerUserId">Quién compra, para el evento de dominio.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <returns>
  /// El producto actualizado y si la compra ya se había ejecutado con esta misma
  /// intención; traducir ese hecho a una cabecera es cosa del controller.
  /// </returns>
  Task<CommandOutcome<ProductDto>> BuyAsync(
      BuyProductDto dto, CommandIntent intent, string? buyerUserId = null, CancellationToken ct = default);

}
