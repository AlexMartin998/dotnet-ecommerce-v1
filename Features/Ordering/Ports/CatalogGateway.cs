using ApiEcommerce.Features.Catalog.Repository;

namespace ApiEcommerce.Features.Ordering.Ports;


/// <summary>
/// Adaptador contra el catálogo. Es el único punto del slice que conoce <c>Catalog</c>.
/// </summary>
/// <remarks>
/// Si el catálogo pasa a ser otro servicio, solo cambia esta clase.
/// </remarks>
public sealed class CatalogGateway(IProductVariantRepository variants, ILogger<CatalogGateway> logger)
    : ICatalogGateway
{
  public async Task<TakeResult> TryTakeAsync(
      string sku, int quantity, CancellationToken ct = default)
  {
    var variant = await variants.GetBySkuAsync(sku, ct);

    if (variant?.Product is null) return TakeResult.Failed(TakeFailure.NotFound);

    if (!variant.IsActive) return TakeResult.Failed(TakeFailure.Unavailable);

    // Comprobar y descontar van en la misma sentencia SQL: separarlos dejaría hueco a otra
    // compra. Si alguien la desactiva justo ahora, la sentencia tampoco la vende (IsActive
    // va en el WHERE) y sale como falta de stock: el cliente vuelve a cotizar y lo ve.
    if (!await variants.TryDecrementStockAsync(variant.Id, quantity, ct))
      return TakeResult.Failed(TakeFailure.InsufficientStock);

    // Se copia lo que la orden congela: a partir de aquí el producto puede cambiar.
    return TakeResult.Taken(new OrderableItem(
        variant.ProductId, variant.Id, variant.SKU, variant.Product.Name, variant.Size, variant.Product.Price));
  }

  public async Task<QuotableItem?> PeekAsync(string sku, CancellationToken ct = default)
  {
    var variant = await variants.GetBySkuAsync(sku, ct);

    return variant?.Product is null
        ? null
        : new QuotableItem(
            variant.ProductId, variant.SKU, variant.Product.Name, variant.Size,
            variant.Product.Price, variant.Stock, variant.IsActive);
  }

  public async Task ReturnAsync(int? variantId, string sku, int quantity, CancellationToken ct = default)
  {
    if (variantId is int id)
    {
      await variants.IncrementStockAsync(id, quantity, ct);
      return;
    }

    // Línea de antes de las variantes: su SKU era el del producto, que es el de su variante
    // sin talla. Si ya no casa con ninguna, no hay a dónde devolver, y se dice.
    if (!await variants.IncrementStockBySkuAsync(sku, quantity, ct))
      logger.LogWarning(
          "Could not return {Quantity} unit(s) of {Sku}: no variant has that SKU", quantity, sku);
  }
}
