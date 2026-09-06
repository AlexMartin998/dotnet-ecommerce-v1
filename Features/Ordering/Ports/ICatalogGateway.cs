namespace ApiEcommerce.Features.Ordering.Ports;


/// <summary>
/// Lo único que <c>Ordering</c> necesita del catálogo: saber qué se vende y apartarlo.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Es un <b>puerto de este slice</b>, no una referencia a <c>Catalog</c>. La diferencia
/// importa: así el servicio de órdenes habla de "apartar una unidad de este SKU" y no
/// conoce ni <c>Product</c>, ni <c>IProductRepository</c>, ni cómo se descuenta el stock.
/// Toda la dependencia hacia el otro contexto queda confinada a <b>una sola clase</b>
/// —el adaptador— y se ve de un vistazo en el composition root.
/// </para>
/// <para>
/// Sin esto, un slice acaba importando tipos del otro por comodidad y en seis meses no hay
/// forma de mover ninguno de los dos: es exactamente la dispersión que el vertical slicing
/// venía a evitar, solo que con carpetas bonitas.
/// </para>
/// </remarks>
public interface ICatalogGateway
{
  /// <summary>
  /// Aparta <paramref name="quantity"/> unidades del SKU y devuelve lo que hay que copiar
  /// en la orden.
  /// </summary>
  /// <remarks>
  /// Apartar y consultar son <b>la misma operación</b> a propósito. Separarlas sería
  /// read-then-write: entre "¿hay stock?" y "descuéntalo" cabe otra compra, y se vendería
  /// dos veces la última unidad.
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
