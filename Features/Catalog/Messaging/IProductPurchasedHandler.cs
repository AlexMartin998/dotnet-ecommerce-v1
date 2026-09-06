using ApiEcommerce.Features.Catalog.Events;

namespace ApiEcommerce.Features.Catalog.Messaging;


/// <summary>
/// Qué se hace cuando se compra un producto: el efecto, separado del transporte.
/// </summary>
/// <remarks>
/// Es una interfaz aparte y no un método del consumidor para poder sustituirla por una
/// que falle y cubrir con un test que la marca y el efecto se deshacen juntos.
/// </remarks>
public interface IProductPurchasedHandler
{
  /// <summary>
  /// Reacciona a la compra. Corre dentro de la transacción del inbox: si lanza, la marca
  /// de procesado se deshace con él y el mensaje se reintenta.
  /// </summary>
  /// <param name="event">El evento ya deserializado.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task HandleAsync(ProductPurchased @event, CancellationToken ct = default);
}


/// <summary>Avisa de stock bajo tras una compra.</summary>
/// <remarks>
/// No debe hacer llamadas de red: lo que corre dentro de la transacción del inbox tiene
/// que poder deshacerse con ella. Un efecto externo se emite como otro evento del outbox.
/// </remarks>
public sealed class LowStockNotifier(ILogger<LowStockNotifier> logger) : IProductPurchasedHandler
{
  /// <summary>Umbral para el aviso de stock bajo.</summary>
  private const int LowStockThreshold = 5;

  public Task HandleAsync(ProductPurchased @event, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(@event);

    logger.LogInformation(
        "Purchase processed: {Quantity} x {Sku} ({ProductName}), remaining {RemainingStock}",
        @event.Quantity, @event.Sku, @event.ProductName, @event.RemainingStock);

    if (@event.RemainingStock <= LowStockThreshold)
      logger.LogWarning(
          "LOW STOCK for {Sku}: only {RemainingStock} left", @event.Sku, @event.RemainingStock);

    return Task.CompletedTask;
  }
}
