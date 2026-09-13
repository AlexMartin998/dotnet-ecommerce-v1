using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Documents;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Ordering.Service;


/// <inheritdoc cref="IOrderService"/>
public sealed class OrderService(
    IOrderRepository repository,
    ICatalogGateway catalog,
    IDocumentStore documents,
    IIdempotentCommandRunner runner,
    ILogger<OrderService> logger) : IOrderService
{
  public Task<CommandOutcome<OrderDto>> PlaceAsync(
      PlaceOrderDto dto, CommandIntent intent, string buyerUserId, string? buyerEmail,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    // El runner abre la transacción y confirma la marca del intento junto al efecto; aquí
    // solo queda la compra. Si otra réplica gana la carrera, devuelve su resultado.
    return runner.RunAsync(intent, dto, async token =>
    {
      var order = await BuildAsync(dto, buyerUserId, buyerEmail, token);

      repository.Add(order);

      // Se guarda aquí para tener el Id; va en la misma transacción que el runner confirma.
      // ⚠️ Colocar la orden NO emite ningún evento: nadie consumiría `order.placed` hoy, y
      // publicar sin cola que lo acepte vuelve como 312 NO_ROUTE y agota el outbox en
      // silencio. Volverá el día que Shipping o las notificaciones lo escuchen.
      await repository.SaveChangesAsync(token);

      logger.LogInformation(
          "Order {Number} placed by {BuyerUserId} for {Total} {Currency}",
          order.Number, buyerUserId, order.Total, order.Currency);

      return ToDto(order);
    }, ct);
  }

  public async Task<OrderDto> GetForBuyerAsync(
      Guid publicId, string buyerUserId, CancellationToken ct = default)
  {
    var order = await repository.FindForBuyerAsync(publicId, buyerUserId, ct)
        // 404 y no 403: "existe pero no es tuya" ya filtra que existe.
        ?? throw new NotFoundAppException("Order", publicId);

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

  public async Task<PagedResult<OrderDto>> GetPagedForAdminAsync(
      PageQuery query, string? number, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var (items, total) = await repository.GetPagedAsync(
        number, query.Skip, query.PageSize, ct);

    return new PagedResult<OrderDto>([.. items.Select(ToDto)], query.Page, query.PageSize, total);
  }

  public async Task<DocumentContent> GetReceiptAsync(
      Guid publicId, string buyerUserId, CancellationToken ct = default)
  {
    // Filtrada por comprador: el comprobante de otro es indistinguible de uno que no existe.
    var order = await repository.FindForBuyerAsync(publicId, buyerUserId, ct)
        ?? throw new NotFoundAppException("Order", publicId);

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


  public async Task AdvanceAsync(
      Guid publicId, UpdateOrderStatusDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    if (!OrderFulfillment.TryResolve(dto.Status, out var to, out var from))
      // 400 y no 422: el cuerpo es válido, lo que no existe es ese destino. Se enumeran los
      // que sí, igual que con los proveedores de pago.
      throw new BadOperationAppException(
          $"'{dto.Status}' is not a reachable order status. " +
          $"Valid targets: {string.Join(", ", OrderFulfillment.Targets)}.");

    if (await repository.TryTransitionAsync(publicId, from, to, ct))
    {
      logger.LogInformation("Order {PublicId} moved to {Status}", publicId, to);
      return;
    }

    // El UPDATE no movió nada. Solo AHORA se lee, y para distinguir tres casos distintos.
    var current = await repository.FindStatusAsync(publicId, ct)
        ?? throw new NotFoundAppException("Order", publicId);

    // Ya estaba donde se la quería dejar: es un reenvío, no un error.
    if (current == to) return;

    throw new CustomAppException(
        "invalid_transition",
        $"An order in '{current.ToString().ToLowerInvariant()}' cannot move to " +
        $"'{to.ToString().ToLowerInvariant()}'.",
        System.Net.HttpStatusCode.Conflict);
  }


  public async Task<OrderStatsDto> GetStatsAsync(CancellationToken ct = default)
  {
    var byStatus = await repository.CountByStatusAsync(ct);

    // Un estado sin filas no aparece en el GROUP BY: sin este 0 el panel mostraría un hueco.
    int Count(OrderStatus status) => byStatus.TryGetValue(status, out var n) ? n : 0;

    return new OrderStatsDto
    {
      Total = byStatus.Values.Sum(),
      Placed = Count(OrderStatus.Placed),
      Paid = Count(OrderStatus.Paid),
      Preparing = Count(OrderStatus.Preparing),
      Shipped = Count(OrderStatus.Shipped),
      Delivered = Count(OrderStatus.Delivered),
      Cancelled = Count(OrderStatus.Cancelled)
    };
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

    // El desglose sale de la misma pieza que usa la cotización: calcularlo aquí otra vez es
    // cómo se acaba cobrando un total distinto del que el cliente vio en el carrito.
    var totals = OrderPricing.For(items.Select(i => i.LineTotal));

    var number = $"ORD-{DateTime.Now:yyyy}-{await repository.NextNumberAsync(ct):D6}";

    return new Order
    {
      Number = number,
      BuyerUserId = buyerUserId,
      // Nace sin pagar: quien la mueve es un cobro capturado, nunca esta llamada.
      Status = OrderStatus.Placed,
      Currency = OrderPricing.Currency,
      Subtotal = totals.Subtotal,
      Discount = totals.Discount,
      Tax = totals.Tax,
      Shipping = totals.Shipping,
      Total = totals.Total,
      CustomerName = dto.CustomerName.Trim(),
      CustomerEmail = buyerEmail,
      CustomerPhone = dto.CustomerPhone?.Trim(),
      ShippingAddress = dto.ShippingAddress?.Trim(),
      Items = items
    };
  }

  private static OrderDto ToDto(Order order) => new()
  {
    PublicId = order.PublicId,
    Number = order.Number,
    BuyerUserId = order.BuyerUserId,
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
