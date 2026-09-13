using ApiEcommerce.Data;
using ApiEcommerce.Features.Payments.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Features.Payments.Repository;


/// <inheritdoc cref="IPaymentRepository"/>
public sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
  /// <summary>Nombre de la secuencia declarada en <c>AppDbContext</c>.</summary>
  public const string ReferenceSequence = "PaymentReferences";

  public async Task<long> NextReferenceAsync(CancellationToken ct = default)
  {
    // Igual que el número de orden: atómica, no bloquea, y puede dejar huecos.
    var result = await db.Database
        .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR {ReferenceSequence} AS [Value]")
        .ToListAsync(ct);

    return result[0];
  }

  public void Add(Payment payment) => db.Payments.Add(payment);

  public Task<Payment?> FindForBuyerAsync(Guid publicId, string buyerUserId, CancellationToken ct = default)
      => db.Payments
          .AsNoTracking()
          .FirstOrDefaultAsync(p => p.PublicId == publicId && p.BuyerUserId == buyerUserId, ct);

  public Task<Payment?> FindByProviderIdAsync(
      PaymentProvider provider, string providerPaymentId, CancellationToken ct = default)
      // Rastreado a propósito: quien llama viene a cambiarle el estado.
      => db.Payments.FirstOrDefaultAsync(
          p => p.Provider == provider && p.ProviderPaymentId == providerPaymentId, ct);

  public Task<bool> HasLivePaymentAsync(int orderId, CancellationToken ct = default)
      // Un pago fallido no bloquea: se pide otro. Uno pendiente sí, o el comprador acabaría
      // con dos intentos abiertos en la pasarela sobre la misma orden.
      => db.Payments.AnyAsync(
          p => p.OrderId == orderId
               && (p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.Captured), ct);

  public async Task<(IReadOnlyList<Payment> Items, int Total)> GetPagedForBuyerAsync(
      string buyerUserId, int skip, int take, CancellationToken ct = default)
      => await PageAsync(db.Payments.AsNoTracking().Where(p => p.BuyerUserId == buyerUserId), skip, take, ct);

  public async Task<(IReadOnlyList<Payment> Items, int Total)> GetPagedAsync(
      string? referencePrefix, int skip, int take, CancellationToken ct = default)
  {
    var query = db.Payments.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(referencePrefix))
    {
      // Por prefijo: un LIKE '%x%' no puede usar IX_Payments_Reference.
      var prefix = referencePrefix.Trim();

      query = query.Where(p => p.Reference.StartsWith(prefix));
    }

    return await PageAsync(query, skip, take, ct);
  }

  public Task<bool> WasWebhookProcessedAsync(string eventId, CancellationToken ct = default)
      => db.ProcessedWebhookEvents.AsNoTracking().AnyAsync(e => e.Id == eventId, ct);

  public void MarkWebhookProcessed(ProcessedWebhookEvent processed)
      => db.ProcessedWebhookEvents.Add(processed);

  public bool IsDuplicateWebhook(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);

    // Se recorre la cadena y se filtra por número, no por tipo: un catch de
    // DbUpdateException a secas se tragaría timeouts y deadlocks.
    for (var current = exception; current is not null; current = current.InnerException)
      if (current is SqlException { Number: 2601 or 2627 }) return true;

    return false;
  }

  public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

  private static async Task<(IReadOnlyList<Payment> Items, int Total)> PageAsync(
      IQueryable<Payment> query, int skip, int take, CancellationToken ct)
  {
    var total = await query.CountAsync(ct);

    var items = await query
        .OrderByDescending(p => p.CreatedAt)
        .ThenByDescending(p => p.Id)   // desempate estable: CreatedAt no es único
        .Skip(skip)
        .Take(take)
        .ToListAsync(ct);

    return (items, total);
  }
}
