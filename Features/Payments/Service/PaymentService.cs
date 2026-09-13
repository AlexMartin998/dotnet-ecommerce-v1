using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments.Dtos;
using ApiEcommerce.Features.Payments.Events;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Features.Payments.Ports;
using ApiEcommerce.Features.Payments.Repository;
using ApiEcommerce.Shared.Db;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Payments.Service;


/// <inheritdoc cref="IPaymentService"/>
public sealed class PaymentService(
    IPaymentRepository repository,
    IOrderingGateway orders,
    IPaymentGatewayRegistry gateways,
    IEventOutbox outbox,
    ITransactionRunner transactions,
    IIdempotentCommandRunner runner,
    ILogger<PaymentService> logger) : IPaymentService
{
  public Task<CommandOutcome<PaymentDto>> StartAsync(
      StartPaymentDto dto, CommandIntent intent, string buyerUserId, string? buyerEmail,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var provider = ParseProvider(dto.Provider);

    // Fuera de la transacción: si el proveedor no existe no hay nada que deshacer, y así el
    // 400 no paga el coste de abrirla.
    var gateway = gateways.For(provider);

    return runner.RunAsync(intent, dto, async token =>
    {
      // [Required] lo garantiza en HTTP; el Value lanzaría ante un llamador que se lo salte.
      var orderPublicId = dto.OrderPublicId!.Value;

      var order = await orders.FindPayableAsync(orderPublicId, buyerUserId, token)
          // 404 y no 403: la orden de otro es indistinguible de una que no existe.
          ?? throw new NotFoundAppException("Order", orderPublicId);

      if (order.AlreadyPaid)
        throw new ConflictAppException($"Order '{order.Number}' is not awaiting payment.");

      if (await repository.HasLivePaymentAsync(order.Id, token))
        throw new ConflictAppException($"Order '{order.Number}' already has a payment in progress.");

      var payment = new Payment
      {
        Reference = $"PAY-{DateTime.Now:yyyy}-{await repository.NextReferenceAsync(token):D6}",
        OrderId = order.Id,
        OrderPublicId = order.PublicId,
        OrderNumber = order.Number,
        BuyerUserId = buyerUserId,
        Provider = provider,
        // Congelado de la orden: lo que se cobra no cambia porque la orden cambie.
        Amount = order.Total,
        Currency = order.Currency,
        Status = PaymentStatus.Pending
      };

      repository.Add(payment);

      // La llamada a la pasarela va DENTRO de la transacción, como el PDF del comprobante:
      // si el commit falla queda un intento huérfano en la pasarela, que es basura, frente a
      // un cobro sin fila que lo recuerde, que es dinero perdido.
      var created = await gateway.CreateIntentAsync(
          new PaymentRequest(payment.Reference, payment.Amount, payment.Currency, order.Number, buyerEmail),
          token);

      payment.ProviderPaymentId = created.ProviderPaymentId;

      await repository.SaveChangesAsync(token);

      logger.LogInformation(
          "Payment {Reference} started for order {Number} with {Provider}",
          payment.Reference, order.Number, provider);

      // El clientSecret no se persiste: es un secreto de un solo uso y guardarlo lo haría
      // filtrable por un listado.
      return ToDto(payment) with { ClientSecret = created.ClientSecret };
    }, ct);
  }

  public async Task HandleWebhookAsync(
      PaymentProvider provider, string rawPayload, string? signatureHeader,
      CancellationToken ct = default)
  {
    // Lanza 400 si la firma no cuadra o caducó: la firma ES la autenticación del webhook.
    var @event = gateways.For(provider).ParseEvent(rawPayload, signatureHeader);

    // Un evento que no nos mueve nada se ignora en silencio, que no es lo mismo que fallar:
    // devolver error haría que la pasarela lo reintentara para siempre.
    if (@event is not { } notification) return;

    // Atajo barato; la garantía es la clave primaria de más abajo.
    if (await repository.WasWebhookProcessedAsync(notification.EventId, ct))
    {
      logger.LogInformation("Webhook {EventId} already processed; ignoring", notification.EventId);
      return;
    }

    try
    {
      await transactions.ExecuteAsync(async token =>
      {
        repository.MarkWebhookProcessed(new ProcessedWebhookEvent
        {
          Id = notification.EventId,
          Provider = provider,
          Type = notification.Type
        });

        await ApplyAsync(provider, notification, token);

        // El choque de clave primaria sale aquí, y arrastra al efecto en el rollback.
        await repository.SaveChangesAsync(token);

        return true;
      }, ct);
    }
    catch (Exception ex) when (repository.IsDuplicateWebhook(ex))
    {
      // Otra réplica procesó el mismo reenvío a la vez y confirmó primero.
      logger.LogInformation("Webhook {EventId} was processed concurrently", notification.EventId);
    }
  }

  public async Task<PaymentDto> GetForBuyerAsync(
      Guid publicId, string buyerUserId, CancellationToken ct = default)
      => ToDto(await repository.FindForBuyerAsync(publicId, buyerUserId, ct)
               ?? throw new NotFoundAppException("Payment", publicId));

  public async Task<PagedResult<PaymentDto>> GetPagedForBuyerAsync(
      PageQuery query, string buyerUserId, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var (items, total) = await repository.GetPagedForBuyerAsync(
        buyerUserId, query.Skip, query.PageSize, ct);

    return new PagedResult<PaymentDto>([.. items.Select(ToDto)], query.Page, query.PageSize, total);
  }

  public async Task<PagedResult<PaymentDto>> GetPagedForAdminAsync(
      PageQuery query, string? reference, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var (items, total) = await repository.GetPagedAsync(
        reference, query.Skip, query.PageSize, ct);

    return new PagedResult<PaymentDto>([.. items.Select(ToDto)], query.Page, query.PageSize, total);
  }


  // ---- el efecto del webhook ----------------------------------------------

  private async Task ApplyAsync(
      PaymentProvider provider, GatewayEvent notification, CancellationToken ct)
  {
    var payment = await repository.FindByProviderIdAsync(
        provider, notification.ProviderPaymentId, ct);

    if (payment is null)
    {
      // Se deja marcado el evento igual: reintentarlo no hará aparecer un pago que no es
      // nuestro, y sin la marca la pasarela lo reenviaría durante días.
      logger.LogWarning(
          "Webhook {EventId} refers to unknown {Provider} payment {ProviderPaymentId}",
          notification.EventId, provider, notification.ProviderPaymentId);
      return;
    }

    // Un pago ya resuelto no vuelve atrás: los eventos de la pasarela pueden llegar
    // desordenados, y un "failed" viejo no puede deshacer un cobro confirmado.
    if (payment.Status != PaymentStatus.Pending)
    {
      logger.LogInformation(
          "Payment {Reference} is already {Status}; ignoring {Type}",
          payment.Reference, payment.Status, notification.Type);
      return;
    }

    payment.Status = notification.Status;
    payment.FailureReason = notification.FailureReason;

    if (notification.Status != PaymentStatus.Captured)
    {
      logger.LogWarning(
          "Payment {Reference} failed: {Reason}", payment.Reference, notification.FailureReason);
      return;
    }

    // En la MISMA transacción que el cambio de estado: si el evento se publicara aparte,
    // habría cobros confirmados que ninguna orden llega a ver.
    await outbox.EnqueueAsync(new PaymentCaptured(
        payment.Id, payment.Reference, payment.OrderId, payment.OrderNumber,
        payment.BuyerUserId, payment.Amount, payment.Currency, DateTime.Now), ct);

    logger.LogInformation(
        "Payment {Reference} captured for order {Number}", payment.Reference, payment.OrderNumber);
  }

  /// <summary>El proveedor que pide el cliente, como enum.</summary>
  /// <remarks>
  /// Un nombre desconocido es un 400 con la lista, no un 500: el texto lo escribe el cliente.
  /// </remarks>
  private static PaymentProvider ParseProvider(string provider)
      => Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var parsed)
          ? parsed
          : throw new CustomAppException(
              "unknown_payment_provider",
              $"Unknown payment provider '{provider}'. Known: {string.Join(", ", Enum.GetNames<PaymentProvider>())}.",
              System.Net.HttpStatusCode.BadRequest);

  private static PaymentDto ToDto(Payment payment) => new()
  {
    PublicId = payment.PublicId,
    Reference = payment.Reference,
    OrderPublicId = payment.OrderPublicId,
    OrderNumber = payment.OrderNumber,
    BuyerUserId = payment.BuyerUserId,
    Provider = payment.Provider.ToString().ToLowerInvariant(),
    Status = payment.Status.ToString().ToLowerInvariant(),
    Amount = payment.Amount,
    Currency = payment.Currency,
    FailureReason = payment.FailureReason,
    CreatedAt = payment.CreatedAt
  };
}
