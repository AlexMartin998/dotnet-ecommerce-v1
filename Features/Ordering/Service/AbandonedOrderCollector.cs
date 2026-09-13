using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Features.Payments;
using ApiEcommerce.Shared.Db;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Service;


/// <inheritdoc cref="IAbandonedOrderCollector"/>
public sealed class AbandonedOrderCollector(
    IOrderRepository orders,
    ICatalogGateway catalog,
    ITransactionRunner transactions,
    IOptions<PaymentOptions> options,
    ILogger<AbandonedOrderCollector> logger) : IAbandonedOrderCollector
{
  /// <summary>Órdenes por pasada, para que una acumulación no bloquee la tabla.</summary>
  private const int BatchSize = 200;

  private readonly PaymentOptions _options = options.Value;

  public async Task<int> CollectAsync(CancellationToken ct = default)
  {
    if (_options.ReservationMinutes <= 0) return 0;

    var cutoff = DateTime.Now.AddMinutes(-_options.ReservationMinutes);

    var abandoned = await orders.FindAwaitingPaymentBeforeAsync(cutoff, BatchSize, ct);

    if (abandoned.Count == 0) return 0;

    var cancelled = 0;

    foreach (var order in abandoned)
    {
      if (ct.IsCancellationRequested) break;

      // Una transacción POR ORDEN y no una para el lote: una orden que falle al devolver no
      // puede impedir que se recuperen las demás.
      if (await CancelAsync(order.Id, order.Items.Select(i => (i.VariantId, i.Sku, i.Quantity)).ToList(), ct))
        cancelled++;
    }

    if (cancelled > 0)
      logger.LogInformation(
          "Cancelled {Count} order(s) that went unpaid for more than {Minutes} minutes",
          cancelled, _options.ReservationMinutes);

    return cancelled;
  }

  private async Task<bool> CancelAsync(
      int orderId, IReadOnlyList<(int? VariantId, string Sku, int Quantity)> lines, CancellationToken ct)
      => await transactions.ExecuteAsync(async token =>
      {
        // La transición manda: si otro la movió entre la lectura y esto —un webhook que
        // llegó tarde—, `false` y no se devuelve nada. Es lo que hace idempotente al
        // recolector y lo que impide devolver el stock de una orden ya pagada.
        if (!await orders.TryCancelAsync(orderId, token)) return false;

        // Mismo orden global que al comprar, POR SKU (OrderService ordena las líneas así).
        // Hasta planning/27 esto iba por ProductId, que NO es el orden de la compra: cancelar
        // y comprar a la vez podían tomar los mismos locks en orden cruzado.
        foreach (var (variantId, sku, quantity) in lines.OrderBy(line => line.Sku, StringComparer.OrdinalIgnoreCase))
          await catalog.ReturnAsync(variantId, sku, quantity, token);

        return true;
      }, ct);

  // // Hasta planning/27: devolvía por producto y en orden de ProductId.
  // private async Task<bool> CancelAsync(
  //     int orderId, IReadOnlyList<(int ProductId, int Quantity)> lines, CancellationToken ct)
  //     => await transactions.ExecuteAsync(async token =>
  //     {
  //       if (!await orders.TryCancelAsync(orderId, token)) return false;
  //
  //       foreach (var (productId, quantity) in lines.OrderBy(line => line.ProductId))
  //         await catalog.ReturnAsync(productId, quantity, token);
  //
  //       return true;
  //     }, ct);
}
