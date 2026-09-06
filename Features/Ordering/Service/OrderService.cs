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
        // Idempotencia DENTRO de la transacción: la marca y el efecto se confirman juntos
        // o no se confirma ninguno. Es la garantía de planning/17, reutilizada tal cual.
        if (await commands.FindResultAsync<OrderDto>(intent, dto, token) is { } already)
          return new CommandOutcome<OrderDto>(already, WasReplayed: true);

        var order = await BuildAsync(dto, buyerUserId, buyerEmail, token);

        repository.Add(order);

        // Se guarda ANTES de emitir el evento porque el evento necesita el Id, que lo
        // asigna la base. Sigue siendo atómico: los dos SaveChanges van dentro de la
        // misma transacción del runner.
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
      // Otra réplica cerró la misma compra a la vez y confirmó primero. Nuestra
      // transacción entera se deshizo —stock incluido—, así que basta con devolver lo
      // que hizo el ganador.
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
        // 404 y no 403: decir "existe pero no es tuya" ya filtra que existe, y con ids
        // correlativos eso permite contar las órdenes de la tienda desde fuera.
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
    // Misma consulta filtrada por comprador que GetForBuyerAsync: el comprobante de otro
    // es indistinguible de uno que no existe.
    var order = await repository.FindForBuyerAsync(orderId, buyerUserId, ct)
        ?? throw new NotFoundAppException("Order", orderId.ToString());

    if (order.ReceiptDocumentKey is null)
      // ⚠️ DOS códigos distintos, y esa es toda la razón de que `ReceiptStatus` sea una
      // columna y no un booleano derivado: "todavía no" y "ya no va a estar" se responden
      // igual de mal con el mismo código. `receipt_not_ready` significa «vuelve en un
      // momento» y un cliente lo reintenta; devolverlo para un comprobante que murió en la
      // DLQ lo deja haciendo polling eterno sobre algo que no va a existir.
      //
      // Los dos son 409 y no 404 porque la ORDEN existe, y CustomAppException y no
      // ConflictAppException porque el `code` es parte del contrato: es por lo que el
      // cliente los distingue, y ConflictAppException fija el suyo en "conflict".
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

    // El almacén devuelve null —no lanza— cuando la clave ya no está: es una condición
    // tratable (un borrado, una migración de infraestructura a medias), no un fallo del
    // sistema. Aquí sí es un 404: la orden existe pero su documento se ha perdido, y
    // decirlo es más honesto que un 500.
    var content = await documents.OpenAsync(order.ReceiptDocumentKey, ct)
        ?? throw new NotFoundAppException("Receipt", order.Number);

    // ⚠️ El nombre de la descarga lo pone el DOMINIO, no el almacén. Visto ejecutando: el
    // fichero llegaba como `cdfdcf87c326aadb22845f2f46c8c691.pdf` —la clave opaca, que es
    // lo único que el almacén sabe de él—, y con eso en la carpeta de descargas nadie
    // sabe de qué compra era. Peor: filtra la forma de las claves. Aquí sí se sabe cómo se
    // llama: es el número de la orden.
    return content with { FileName = $"{order.Number}.pdf" };
  }


  // ---- construcción -------------------------------------------------------

  private async Task<Order> BuildAsync(
      PlaceOrderDto dto, string buyerUserId, string? buyerEmail, CancellationToken ct)
  {
    // ⚠️ Se agrupan las líneas repetidas ANTES de apartar stock. Sin esto, un carrito con
    // el mismo SKU dos veces produciría dos líneas idénticas en el comprobante y dos
    // descuentos separados: cuadra en total, pero el documento queda raro y el cliente
    // llama preguntando.
    var lines = dto.Items
        .GroupBy(i => i.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
        .Select(g => (Sku: g.Key, Quantity: g.Sum(i => i.Quantity)))
        // ⚠️ Y se ORDENAN por SKU, que no es cosmética: cada descuento toma un lock
        // exclusivo de la fila del producto y lo mantiene hasta el commit, que aquí está
        // lejos (secuencia, INSERT de la orden, outbox, marca del comando). Recorrerlas en
        // el orden que mandó el CLIENTE es pedir un deadlock: A compra [1,2] y B compra
        // [2,1] a la vez, cada uno bloquea el primero y espera el del otro. Con un orden
        // total y global de adquisición, el deadlock deja de ser posible por construcción.
        // Se sobreviviría —1205 es transitorio y EF reintenta— pero rehaciendo la compra
        // entera, y con contención alta se agotan los reintentos y sale un 500.
        .OrderBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var items = new List<OrderItem>(lines.Count);

    foreach (var (sku, quantity) in lines)
    {
      var taken = await catalog.TryTakeAsync(sku, quantity, ct)
          // Un solo mensaje para "no existe" y "no hay bastante": distinguirlos convierte
          // el checkout en un inventario consultable desde fuera.
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
      // Descuento, impuestos y envío quedan a cero: no hay reglas de negocio que los
      // calculen todavía. Están en el modelo y en el comprobante porque el desglose es
      // parte del documento, y añadirlos después obligaría a migrar datos.
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
