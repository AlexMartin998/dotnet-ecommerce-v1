namespace ApiEcommerce.Features.Ordering.Ports;


/// <summary>
/// Lo único que <c>Ordering</c> necesita del catálogo: saber qué se vende y apartarlo.
/// </summary>
/// <remarks>
/// Es un puerto de este slice, no una referencia a <c>Catalog</c>: el servicio de órdenes no
/// conoce <c>Product</c> ni cómo se descuenta el stock, y toda la dependencia hacia el otro
/// contexto queda confinada al adaptador.
/// </remarks>
public interface ICatalogGateway
{
  /// <summary>
  /// Aparta <paramref name="quantity"/> unidades del SKU y devuelve lo que hay que copiar
  /// en la orden.
  /// </summary>
  /// <remarks>
  /// Apartar y consultar son la misma operación a propósito: separarlas sería
  /// read-then-write y se vendería dos veces la última unidad.
  /// </remarks>
  /// <returns><c>null</c> si el SKU no existe o no hay stock suficiente.</returns>
  Task<OrderableItem?> TryTakeAsync(string sku, int quantity, CancellationToken ct = default);
}


/// <summary>Lo que la orden copia del catálogo, en el momento de comprar.</summary>
/// <param name="ProductId">Referencia informativa: la orden sobrevive si el producto se borra.</param>
/// <param name="Sku">SKU tal y como estaba.</param>
/// <param name="Name">Nombre tal y como estaba.</param>
/// <param name="UnitPrice">Precio tal y como estaba.</param>
public readonly record struct OrderableItem(int ProductId, string Sku, string Name, decimal UnitPrice);
