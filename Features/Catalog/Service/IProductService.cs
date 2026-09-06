using ApiEcommerce.Shared.Storage;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Features.Catalog.Service;


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
  /// 404 si el SKU no existe; 409 si el stock es insuficiente; 422 si la intención se
  /// reusó con otra compra distinta.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <paramref name="intent"/> <b>no tiene valor por defecto, a propósito</b>. La
  /// idempotencia de esta operación es una invariante suya, no algo que dependa de que
  /// el controller lleve puesto un atributo: llamarla desde un job o desde otro endpoint
  /// sin declarar la intención perdería la garantía en silencio. Es exactamente el bug
  /// que motivó mover la transacción aquí, y se evita igual — obligando a decidir.
  /// Renunciar se escribe: <see cref="CommandIntent.None"/>.
  /// </para>
  /// </remarks>
  /// <param name="dto">Qué se compra y cuánto.</param>
  /// <param name="intent">La identidad de este intento de compra.</param>
  /// <param name="buyerUserId">Quién compra, para el evento de dominio.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <returns>
  /// El producto actualizado, y si la compra <b>ya se había ejecutado</b> con esta misma
  /// intención. Ese segundo dato es un hecho de negocio; traducirlo a una cabecera es
  /// cosa del adaptador.
  /// </returns>
  Task<CommandOutcome<ProductDto>> BuyAsync(
      BuyProductDto dto, CommandIntent intent, string? buyerUserId = null, CancellationToken ct = default);

}
