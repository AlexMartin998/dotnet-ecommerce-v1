using ApiEcommerce.Shared.Paging;
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

  /// <summary>Productos de la categoría con ese slug. Si no existe, lanza <c>NotFoundAppException</c>.</summary>
  Task<IEnumerable<ProductDto>> GetForCategorySlugAsync(string categorySlug, CancellationToken ct = default);

  /// <summary>Una página de los productos de la categoría con ese slug. 404 si no existe.</summary>
  Task<PagedResult<ProductDto>> GetPagedForCategorySlugAsync(
      string categorySlug, PageQuery query, CancellationToken ct = default);

  /// <summary>Una página de la búsqueda por nombre. Sin nombre o sin resultados, página vacía.</summary>
  Task<PagedResult<ProductDto>> SearchPagedAsync(string name, PageQuery query, CancellationToken ct = default);

  /// <summary>Búsqueda por coincidencia parcial de nombre. Sin resultados devuelve lista vacía, no 404.</summary>
  Task<IEnumerable<ProductDto>> SearchAsync(string name, CancellationToken ct = default);

  /// <summary>Añade una imagen pública al producto, al final de la lista.</summary>
  /// <remarks>
  /// Añade y no reemplaza: un producto de catálogo se enseña con varias fotos. Es una
  /// operación aparte y no un campo del PATCH porque subir un binario exige
  /// <c>multipart/form-data</c> y cambiaría el content-type de todos los clientes.
  /// </remarks>
  Task<ProductDto> AddImageAsync(int id, FileUpload upload, CancellationToken ct = default);

  /// <summary>Quita una imagen del producto y borra su fichero del disco.</summary>
  Task<ProductDto> RemoveImageAsync(int id, int imageId, CancellationToken ct = default);

  /// <summary>El producto con ese slug, que es lo que el front usa en la URL pública.</summary>
  Task<ProductDto> GetBySlugAsync(string slug, CancellationToken ct = default);

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


  /// <summary>Contadores del catálogo para el panel. Solo administración.</summary>
  Task<ProductStatsDto> GetStatsAsync(CancellationToken ct = default);
}
