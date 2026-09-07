namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>
/// Cancela las órdenes que nadie pagó y devuelve al catálogo el stock que tenían apartado.
/// </summary>
/// <remarks>
/// Es la contrapartida de que la orden nazca sin pagar: al colocarla ya se descuenta el
/// stock, así que sin esto un carrito abandonado lo retiene para siempre. El efecto vive
/// aquí y no dentro del <c>BackgroundService</c> para poder probarlo sin esperar horas.
/// </remarks>
public interface IAbandonedOrderCollector
{
  /// <summary>Una pasada. Devuelve cuántas órdenes se cancelaron.</summary>
  Task<int> CollectAsync(CancellationToken ct = default);
}
