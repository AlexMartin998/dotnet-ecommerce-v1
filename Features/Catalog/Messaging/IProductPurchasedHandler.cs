using ApiEcommerce.Features.Catalog.Events;

namespace ApiEcommerce.Features.Catalog.Messaging;


/// <summary>
/// Qué se hace cuando se compra un producto. El <b>efecto</b>, separado del transporte.
/// </summary>
/// <remarks>
/// <para>
/// Estaba dentro del consumidor como un método privado, y eso tenía un coste concreto:
/// no había forma de hacerlo fallar, así que el arreglo del P0 —marca y efecto en la
/// misma transacción— se quedó <b>sin un test que lo cubriera</b>. Un efecto inyectable
/// permite escribir el único test que de verdad importa: el del efecto que revienta.
/// </para>
/// <para>
/// Y de paso deja al consumidor siendo lo que debe ser: fontanería AMQP (ack, reintentos,
/// DLQ) que no sabe qué significa el mensaje que transporta.
/// </para>
/// </remarks>
public interface IProductPurchasedHandler
{
  /// <summary>
  /// Reacciona a la compra. <b>Corre dentro de la transacción del inbox</b>, así que si
  /// lanza, la marca de «procesado» se deshace con él y el mensaje se reintenta.
  /// </summary>
  /// <param name="event">El evento ya deserializado.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task HandleAsync(ProductPurchased @event, CancellationToken ct = default);
}


/// <summary>Avisa de stock bajo.</summary>
/// <remarks>
/// En un sistema real esto notificaría a compras, escribiría una proyección de lectura o
/// llamaría a un webhook. ⚠️ Si algún día hace una llamada de red, deja de ser
/// transaccional: lo que corre dentro de la transacción del inbox tiene que poder
/// deshacerse con ella. Un efecto externo se emite como <b>otro</b> evento del outbox.
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
