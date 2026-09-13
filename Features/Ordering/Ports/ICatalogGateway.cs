namespace ApiEcommerce.Features.Ordering.Ports;


/// <summary>
/// Lo único que <c>Ordering</c> necesita del catálogo: saber qué se vende y apartarlo.
/// </summary>
/// <remarks>
/// Es un puerto de este slice, no una referencia a <c>Catalog</c>: el servicio de órdenes no
/// conoce <c>Product</c> ni cómo se descuenta el stock, y toda la dependencia hacia el otro
/// contexto queda confinada al adaptador. Desde <c>planning/27</c> el SKU es el de una
/// talla (una variante), pero eso tampoco lo sabe Ordering: para él sigue siendo «un SKU».
/// </remarks>
public interface ICatalogGateway
{
  /// <summary>
  /// Aparta <paramref name="quantity"/> unidades del SKU y devuelve lo que hay que copiar
  /// en la orden, o por qué no se pudo.
  /// </summary>
  /// <remarks>
  /// Apartar y consultar son la misma operación a propósito: separarlas sería
  /// read-then-write y se vendería dos veces la última unidad.
  /// </remarks>
  Task<TakeResult> TryTakeAsync(string sku, int quantity, CancellationToken ct = default);

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

  /// <summary>Devuelve al catálogo lo que una línea había apartado.</summary>
  /// <remarks>
  /// Por id de variante, que la línea guarda justo para poder deshacer. Una línea anterior a
  /// las variantes no lo tiene y se devuelve por su <paramref name="sku"/>.
  /// </remarks>
  Task ReturnAsync(int? variantId, string sku, int quantity, CancellationToken ct = default);

  // // Hasta planning/27: el stock era del producto y null no decía por qué.
  // Task<OrderableItem?> TryTakeAsync(string sku, int quantity, CancellationToken ct = default);
  // Task ReturnAsync(int productId, int quantity, CancellationToken ct = default);
}


/// <summary>Por qué no se pudo apartar una línea.</summary>
public enum TakeFailure
{
  None = 0,

  /// <summary>Ningún artículo a la venta responde a ese SKU.</summary>
  NotFound,

  /// <summary>Existe, pero se retiró de la venta (una talla desactivada).</summary>
  Unavailable,

  /// <summary>Existe y está a la venta, pero no hay tantas unidades.</summary>
  InsufficientStock
}


/// <summary>El resultado de apartar: lo apartado, o el motivo del fallo.</summary>
public readonly record struct TakeResult(OrderableItem? Item, TakeFailure Failure)
{
  public static TakeResult Taken(OrderableItem item) => new(item, TakeFailure.None);

  public static TakeResult Failed(TakeFailure failure) => new(null, failure);
}


/// <summary>Lo que la orden copia del catálogo, en el momento de comprar.</summary>
/// <param name="ProductId">Producto al que pertenece.</param>
/// <param name="VariantId">La variante apartada, para devolverla si la orden se cancela.</param>
/// <param name="Sku">SKU tal y como estaba.</param>
/// <param name="Name">Nombre tal y como estaba.</param>
/// <param name="Size">Talla tal y como estaba; <c>null</c> si se vende sin tallas.</param>
/// <param name="UnitPrice">Precio tal y como estaba.</param>
public readonly record struct OrderableItem(
    int ProductId, int VariantId, string Sku, string Name, string? Size, decimal UnitPrice);


/// <summary>Lo que la cotización enseña de un artículo, sin comprometerlo.</summary>
/// <param name="ProductId">Id en el catálogo.</param>
/// <param name="Sku">SKU tal y como está ahora.</param>
/// <param name="Name">Nombre tal y como está ahora.</param>
/// <param name="Size">Talla; <c>null</c> si se vende sin tallas.</param>
/// <param name="UnitPrice">Precio de hoy, que puede cambiar antes de comprar.</param>
/// <param name="Stock">Unidades disponibles en este instante.</param>
/// <param name="IsActive">Si sigue a la venta.</param>
public readonly record struct QuotableItem(
    int ProductId, string Sku, string Name, string? Size, decimal UnitPrice, int Stock, bool IsActive);
