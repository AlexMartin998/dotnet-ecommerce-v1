using ApiEcommerce.Features.Catalog.Repository;

namespace ApiEcommerce.Features.Ordering.Ports;


/// <summary>
/// Adaptador contra el catálogo. <b>Es el único punto de todo el slice que conoce
/// <c>Catalog</c></b>.
/// </summary>
/// <remarks>
/// Si mañana el catálogo es otro servicio, lo que cambia es esta clase —pasaría a hacer
/// una llamada HTTP o a publicar un comando— y ni el servicio de órdenes ni el controller
/// se enteran. Esa es toda la razón de que exista el puerto.
/// </remarks>
public sealed class CatalogGateway(IProductRepository products) : ICatalogGateway
{
  public async Task<OrderableItem?> TryTakeAsync(
      string sku, int quantity, CancellationToken ct = default)
  {
    var product = await products.GetBySkuAsync(sku, ct);

    if (product is null) return null;

    // El descuento y la comprobación ocurren en la MISMA sentencia SQL: entre comprobar
    // y descontar cabe otra compra, y se vendería dos veces la última unidad.
    if (!await products.TryDecrementStockAsync(product.Id, quantity, ct)) return null;

    // Se copia lo que la orden tiene que congelar. A partir de aquí, que el producto
    // cambie de precio o de nombre no altera esta compra.
    return new OrderableItem(product.Id, product.SKU, product.Name, product.Price);
  }
}
