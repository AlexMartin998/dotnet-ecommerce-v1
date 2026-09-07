using ApiEcommerce.Features.Catalog.Repository;

namespace ApiEcommerce.Features.Ordering.Ports;


/// <summary>
/// Adaptador contra el catálogo. Es el único punto del slice que conoce <c>Catalog</c>.
/// </summary>
/// <remarks>
/// Si el catálogo pasa a ser otro servicio, solo cambia esta clase.
/// </remarks>
public sealed class CatalogGateway(IProductRepository products) : ICatalogGateway
{
  public async Task<OrderableItem?> TryTakeAsync(
      string sku, int quantity, CancellationToken ct = default)
  {
    var product = await products.GetBySkuAsync(sku, ct);

    if (product is null) return null;

    // Comprobar y descontar van en la misma sentencia SQL: separarlos dejaría hueco a otra compra.
    if (!await products.TryDecrementStockAsync(product.Id, quantity, ct)) return null;

    // Se copia lo que la orden congela: a partir de aquí el producto puede cambiar.
    return new OrderableItem(product.Id, product.SKU, product.Name, product.Price);
  }
}
