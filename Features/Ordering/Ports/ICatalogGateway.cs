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

  /// <summary>
  /// Mira qué se vende bajo ese SKU y cuánto queda, <b>sin apartar nada</b>.
  /// </summary>
  /// <remarks>
  /// Es lo contrario de <see cref="TryTakeAsync"/> y por eso es un método aparte: cotizar un
  /// carrito que apartara stock dejaría el catálogo a cero con los carritos abandonados.
  /// Lo que devuelve es una foto, no una reserva.
  /// </remarks>
  /// <returns><c>null</c> si el SKU no existe.</returns>
  Task<QuotableItem?> PeekAsync(string sku, CancellationToken ct = default);

  /// <summary>Devuelve al catálogo lo que una orden había apartado.</summary>
  /// <remarks>
  /// Se identifica por id de producto y no por SKU: el SKU pudo cambiar desde la compra, y
  /// la orden guarda el id justo para poder deshacer.
  /// </remarks>
  Task ReturnAsync(int productId, int quantity, CancellationToken ct = default);
}


/// <summary>Lo que la orden copia del catálogo, en el momento de comprar.</summary>
/// <param name="ProductId">Referencia informativa: la orden sobrevive si el producto se borra.</param>
/// <param name="Sku">SKU tal y como estaba.</param>
/// <param name="Name">Nombre tal y como estaba.</param>
/// <param name="UnitPrice">Precio tal y como estaba.</param>
public readonly record struct OrderableItem(int ProductId, string Sku, string Name, decimal UnitPrice);


/// <summary>Lo que la cotización enseña de un artículo, sin comprometerlo.</summary>
/// <param name="ProductId">Id en el catálogo.</param>
/// <param name="Sku">SKU tal y como está ahora.</param>
/// <param name="Name">Nombre tal y como está ahora.</param>
/// <param name="UnitPrice">Precio de hoy, que puede cambiar antes de comprar.</param>
/// <param name="Stock">Unidades disponibles en este instante.</param>
public readonly record struct QuotableItem(
    int ProductId, string Sku, string Name, decimal UnitPrice, int Stock);
