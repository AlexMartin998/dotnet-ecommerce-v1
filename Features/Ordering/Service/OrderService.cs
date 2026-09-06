using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Db;
using ApiEcommerce.Shared.Documents;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Ordering.Service;


/// <inheritdoc cref="IOrderService"/>
public sealed class OrderService(
    IOrderRepository repository,
    ICatalogGateway catalog,
    IDocumentStore documents,
    IEventOutbox outbox,
    ITransactionRunner transactions,
    ICommandLog commands,
    ILogger<OrderService> logger) : IOrderService
{
  public async Task<CommandOutcome<OrderDto>> PlaceAsync(
      PlaceOrderDto dto, CommandIntent intent, string buyerUserId, string? buyerEmail,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    try
    {
      return await transactions.ExecuteAsync(async token =>
      {
        // Idempotencia dentro de la transacción: la marca y el efecto se confirman juntos.
        if (await commands.FindResultAsync<OrderDto>(intent, dto, token) is { } already)
          return new CommandOutcome<OrderDto>(already, WasReplayed: true);

        var order = await BuildAsync(dto, buyerUserId, buyerEmail, token);

        repository.Add(order);

        // Antes del evento porque este necesita el Id; los dos SaveChanges van en la misma transacción.
        await repository.SaveChangesAsync(token);

        await outbox.EnqueueAsync(new OrderPlaced(
            order.Id, order.Number, buyerUserId, order.Total, order.Currency, DateTime.Now), token);

        var result = ToDto(order);

        commands.Record(intent, dto, result);

        await repository.SaveChangesAsync(token);

        logger.LogInformation(
            "Order {Number} placed by {BuyerUserId} for {Total} {Currency}",
            order.Number, buyerUserId, order.Total, order.Currency);

        return new CommandOutcome<OrderDto>(result, WasReplayed: false);
      }, ct);
    }
    catch (Exception ex) when (commands.IsDuplicateIntent(ex))
    {
      // Otra réplica confirmó primero: nuestra transacción se deshizo entera, stock incluido.
      var winner = await commands.FindResultAsync<OrderDto>(intent, dto, ct)
          ?? throw new ConflictAppException(
              "A concurrent request with the same idempotency key is still in progress.");

      return new CommandOutcome<OrderDto>(winner, WasReplayed: true);
    }
  }

  public async Task<OrderDto> GetForBuyerAsync(
      int id, string buyerUserId, CancellationToken ct = default)
  {
    var order = await repository.FindForBuyerAsync(id, buyerUserId, ct)
        // 404 y no 403: "existe pero no es tuya" ya filtra que existe.
        ?? throw new NotFoundAppException("Order", id.ToString());

    return ToDto(order);
  }

  public async Task<PagedResult<OrderDto>> GetPagedForBuyerAsync(
      PageQuery query, string buyerUserId, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var (items, total) = await repository.GetPagedForBuyerAsync(
        buyerUserId, query.Skip, query.PageSize, ct);

    return new PagedResult<OrderDto>([.. items.Select(ToDto)], query.Page, query.PageSize, total);
  }

  public async Task<DocumentContent> GetReceiptAsync(
      int orderId, string buyerUserId, CancellationToken ct = default)
  {
    // Filtrada por comprador: el comprobante de otro es indistinguible de uno que no existe.
    var order = await repository.FindForBuyerAsync(orderId, buyerUserId, ct)
        ?? throw new NotFoundAppException("Order", orderId.ToString());

    if (order.ReceiptDocumentKey is null)
      // Dos códigos porque "todavía no" es reintentable y "ya no va a estar" no lo es.
      // 409 y no 404 porque la orden existe; CustomAppException porque el `code` es contrato.
      throw order.ReceiptStatus == ReceiptStatus.Failed
          ? new CustomAppException(
              "receipt_failed",
              $"The receipt for order '{order.Number}' could not be generated. " +
              "The order itself is valid; contact support to have it reissued.",
              System.Net.HttpStatusCode.Conflict)
          : new CustomAppException(
              "receipt_not_ready",
              $"The receipt for order '{order.Number}' is not ready yet.",
              System.Net.HttpStatusCode.Conflict);

    // El almacén devuelve null si la clave ya no está: la orden existe pero su documento
    // se perdió, y eso es un 404, no un 500.
    var content = await documents.OpenAsync(order.ReceiptDocumentKey, ct)
        ?? throw new NotFoundAppException("Receipt", order.Number);

    // El nombre de la descarga lo pone el dominio: el del almacén es la clave opaca, que
    // no dice de qué compra es y además filtra la forma de las claves.
    return content with { FileName = $"{order.Number}.pdf" };
  }


  // ---- construcción -------------------------------------------------------

  private async Task<Order> BuildAsync(
      PlaceOrderDto dto, string buyerUserId, string? buyerEmail, CancellationToken ct)
  {
    // Se agrupan los SKU repetidos antes de apartar stock: si no, salen líneas duplicadas
    // en el comprobante y dos descuentos separados.
    var lines = dto.Items
        .GroupBy(i => i.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
        .Select(g => (Sku: g.Key, Quantity: g.Sum(i => i.Quantity)))
        // Ordenar por SKU da un orden global de adquisición de locks: sin él, dos compras cruzadas hacen deadlock.
        .OrderBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var items = new List<OrderItem>(lines.Count);

    foreach (var (sku, quantity) in lines)
    {
      var taken = await catalog.TryTakeAsync(sku, quantity, ct)
          // Un solo mensaje para "no existe" y "no hay bastante": distinguirlos haría el
          // inventario consultable desde fuera.
          ?? throw new ConflictAppException($"'{sku}' is not available in the requested quantity.");

      items.Add(new OrderItem
      {
        ProductId = taken.ProductId,
        Sku = taken.Sku,
        Name = taken.Name,
        UnitPrice = taken.UnitPrice,
        Quantity = quantity,
        LineTotal = taken.UnitPrice * quantity
      });
    }

    var subtotal = items.Sum(i => i.LineTotal);

    var number = $"ORD-{DateTime.Now:yyyy}-{await repository.NextNumberAsync(ct):D6}";

    return new Order
    {
      Number = number,
      BuyerUserId = buyerUserId,
      Status = OrderStatus.Paid,
      Subtotal = subtotal,
      // A cero: todavía no hay reglas que los calculen, pero el desglose ya está en el modelo.
      Discount = 0m,
      Tax = 0m,
      Shipping = 0m,
      Total = subtotal,
      CustomerName = dto.CustomerName.Trim(),
      CustomerEmail = buyerEmail,
      CustomerPhone = dto.CustomerPhone?.Trim(),
      ShippingAddress = dto.ShippingAddress?.Trim(),
      Items = items
    };
  }

  private static OrderDto ToDto(Order order) => new()
  {
    Id = order.Id,
    Number = order.Number,
    Status = order.Status.ToString().ToLowerInvariant(),
    Currency = order.Currency,
    Subtotal = order.Subtotal,
    Discount = order.Discount,
    Tax = order.Tax,
    Shipping = order.Shipping,
    Total = order.Total,
    CustomerName = order.CustomerName,
    CustomerEmail = order.CustomerEmail,
    CustomerPhone = order.CustomerPhone,
    ShippingAddress = order.ShippingAddress,
    PlacedAt = order.PlacedAt,
    ReceiptStatus = order.ReceiptStatus.ToString().ToLowerInvariant(),
    Items = [.. order.Items.Select(i => new OrderItemDto
    {
      ProductId = i.ProductId,
      Sku = i.Sku,
      Name = i.Name,
      UnitPrice = i.UnitPrice,
      Quantity = i.Quantity,
      LineTotal = i.LineTotal
    })]
  };
}
