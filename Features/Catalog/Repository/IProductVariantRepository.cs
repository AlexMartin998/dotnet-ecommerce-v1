using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Catalog.Repository;


/// <summary>Las tallas de los productos, y el stock que se aparta y se devuelve.</summary>
public interface IProductVariantRepository : IBaseRepository<ProductVariant>
{
  /// <summary>La variante con ese SKU, con su producto cargado. Sin rastreo.</summary>
  /// <remarks>
  /// Un producto retirado no la devuelve (filtro global): lo que no está a la venta no se
  /// cotiza ni se compra.
  /// </remarks>
  Task<ProductVariant?> GetBySkuAsync(string sku, CancellationToken ct = default);

  /// <summary>Todas las variantes del producto, activas o no, por posición.</summary>
  Task<IReadOnlyList<ProductVariant>> GetForProductAsync(int productId, CancellationToken ct = default);

  /// <summary>La variante rastreada, solo si es de ese producto.</summary>
  Task<ProductVariant?> GetTrackedAsync(int productId, int variantId, CancellationToken ct = default);

  /// <summary>¿Hay ya una variante no retirada con ese SKU, fuera de <paramref name="excludeProductId"/>?</summary>
  /// <remarks>Mira exactamente lo que vigila el índice único filtrado por <c>DeletedAt</c>.</remarks>
  Task<bool> SkuExistsAsync(string sku, int? excludeProductId = null, CancellationToken ct = default);

  /// <summary>
  /// Toma un lock exclusivo sobre las variantes de un producto hasta el final de la
  /// transacción en curso (<c>sp_getapplock</c>).
  /// </summary>
  /// <remarks>
  /// Las reglas de las variantes miran a las HERMANAS («no mezclar la variante sin talla con
  /// tallas activas»), y ningún índice puede expresarlas. Sin serializar, un PATCH que reactiva
  /// y un POST que añade leen los dos el estado previo y los dos escriben: la revisión lo
  /// reprodujo en 23 de 25 intentos. Un applock y no un UPDATE del producto porque tocar su
  /// fila cambiaría su <c>RowVersion</c> y daría 412 a quien edita el producto.
  /// </remarks>
  Task LockProductAsync(int productId, CancellationToken ct = default);

  /// <summary>Cambia el SKU de la variante sin talla del producto, si la tiene.</summary>
  /// <remarks>
  /// En un producto sin tallas los dos SKU son el mismo: es el que el carrito manda. Separarlos
  /// al renombrar dejaba la ficha con un SKU que no se podía comprar.
  /// </remarks>
  Task SyncUnsizedSkuAsync(int productId, string sku, CancellationToken ct = default);

  /// <summary>
  /// Descuenta stock de forma atómica:
  /// <c>UPDATE ... SET Stock = Stock - @q WHERE Id = @id AND IsActive = 1 AND Stock &gt;= @q</c>.
  /// </summary>
  /// <remarks>
  /// El mismo UPDATE condicional que tenía el producto (§7.1), en la tabla donde ahora vive
  /// el stock. <c>IsActive</c> va dentro de la sentencia: desactivar una talla entre la
  /// lectura y el descuento no puede dejar venderla.
  /// </remarks>
  /// <returns><c>false</c> si no había stock suficiente o la talla ya no está activa.</returns>
  Task<bool> TryDecrementStockAsync(int variantId, int quantity, CancellationToken ct = default);

  /// <summary>Devuelve stock a la variante. Suma incondicional.</summary>
  Task IncrementStockAsync(int variantId, int quantity, CancellationToken ct = default);

  /// <summary>Igual, citándola por SKU: para las líneas anteriores a las variantes.</summary>
  /// <returns><c>false</c> si ninguna variante tiene ese SKU.</returns>
  Task<bool> IncrementStockBySkuAsync(string sku, int quantity, CancellationToken ct = default);
}
