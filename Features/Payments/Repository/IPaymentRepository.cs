using ApiEcommerce.Features.Payments.Models;

namespace ApiEcommerce.Features.Payments.Repository;


/// <summary>Acceso a los pagos.</summary>
/// <remarks>
/// No hereda de <c>IBaseRepository&lt;T&gt;</c>: un pago se crea y cambia de estado, nunca
/// se edita ni se borra.
/// </remarks>
public interface IPaymentRepository
{
  /// <summary>Reserva la siguiente referencia de pago.</summary>
  Task<long> NextReferenceAsync(CancellationToken ct = default);

  void Add(Payment payment);

  /// <summary>Un pago del comprador. La de otro es indistinguible de una que no existe.</summary>
  Task<Payment?> FindForBuyerAsync(Guid publicId, string buyerUserId, CancellationToken ct = default);

  /// <summary>El pago que la pasarela identifica con ese id, o <c>null</c> si no lo conocemos.</summary>
  Task<Payment?> FindByProviderIdAsync(
      PaymentProvider provider, string providerPaymentId, CancellationToken ct = default);

  /// <summary>¿Tiene ya esta orden un pago capturado o en curso?</summary>
  Task<bool> HasLivePaymentAsync(int orderId, CancellationToken ct = default);

  Task<(IReadOnlyList<Payment> Items, int Total)> GetPagedForBuyerAsync(
      string buyerUserId, int skip, int take, CancellationToken ct = default);

  /// <summary>Todos los pagos. Solo para administración.</summary>
  Task<(IReadOnlyList<Payment> Items, int Total)> GetPagedAsync(
      string? referencePrefix, int skip, int take, CancellationToken ct = default);

  /// <summary>¿Ya se procesó este evento de webhook?</summary>
  Task<bool> WasWebhookProcessedAsync(string eventId, CancellationToken ct = default);

  /// <summary>Marca el evento. No hace <c>SaveChanges</c>: lo confirma la transacción.</summary>
  void MarkWebhookProcessed(ProcessedWebhookEvent processed);

  /// <summary>¿Es el choque de clave primaria de <c>ProcessedWebhookEvents</c>?</summary>
  bool IsDuplicateWebhook(Exception exception);

  Task SaveChangesAsync(CancellationToken ct = default);
}
