using ApiEcommerce.Data;
using ApiEcommerce.Features.Ordering.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Features.Ordering.Repository;


/// <inheritdoc cref="IOrderRepository"/>
public sealed class OrderRepository(AppDbContext db) : IOrderRepository
{
  /// <summary>Nombre de la secuencia declarada en <c>AppDbContext</c>.</summary>
  public const string NumberSequence = "OrderNumbers";

  public async Task<long> NextNumberAsync(CancellationToken ct = default)
  {
    // `SELECT NEXT VALUE FOR` es atómico y no bloquea; la secuencia no se reinicia por año.
    // Los números pueden tener huecos: una secuencia no se deshace con el rollback, lo que
    // basta para un comprobante interno pero no para una factura legal.
    var result = await db.Database
        .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR {NumberSequence} AS [Value]")
        .ToListAsync(ct);

    return result[0];
  }

  public void Add(Order order) => db.Orders.Add(order);

  public Task<Order?> FindForBuyerAsync(int id, string buyerUserId, CancellationToken ct = default)
      => db.Orders
          .AsNoTracking()
          .Include(o => o.Items)
          .FirstOrDefaultAsync(o => o.Id == id && o.BuyerUserId == buyerUserId, ct);

  public Task<Order?> FindWithItemsAsync(int id, CancellationToken ct = default)
      => db.Orders
          .AsNoTracking()
          .Include(o => o.Items)
          .FirstOrDefaultAsync(o => o.Id == id, ct);

  public async Task<(IReadOnlyList<Order> Items, int Total)> GetPagedForBuyerAsync(
      string buyerUserId, int skip, int take, CancellationToken ct = default)
  {
    var query = db.Orders.AsNoTracking().Where(o => o.BuyerUserId == buyerUserId);

    var total = await query.CountAsync(ct);

    var items = await query
        .OrderByDescending(o => o.PlacedAt)
        .ThenByDescending(o => o.Id)   // desempate estable al paginar órdenes del mismo instante
        .Skip(skip)
        .Take(take)
        .Include(o => o.Items)
        .ToListAsync(ct);

    return (items, total);
  }

  public async Task SetReceiptAsync(int orderId, string documentKey, CancellationToken ct = default)
  {
    // Fuera del árbol de expresión: dentro, DateTime.Now se traduciría a GETDATE().
    var now = DateTime.Now;

    await db.Orders
        .Where(o => o.Id == orderId)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.ReceiptDocumentKey, documentKey)
            .SetProperty(o => o.ReceiptStatus, ReceiptStatus.Available)
            .SetProperty(o => o.ReceiptGeneratedAt, now)
            .SetProperty(o => o.UpdatedAt, now), ct);
  }

  public async Task SetReceiptFailedAsync(int orderId, CancellationToken ct = default)
  {
    var now = DateTime.Now;

    await db.Orders
        // Solo si no tiene comprobante: un replay que termina bien puede cruzarse con el
        // mensaje original agotando sus intentos.
        .Where(o => o.Id == orderId && o.ReceiptDocumentKey == null)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.ReceiptStatus, ReceiptStatus.Failed)
            .SetProperty(o => o.UpdatedAt, now), ct);
  }

  public async Task<IReadOnlySet<string>> FindReferencedKeysAsync(
      IReadOnlyCollection<string> keys, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(keys);

    if (keys.Count == 0) return new HashSet<string>(StringComparer.Ordinal);

    var referenced = await db.Orders
        .AsNoTracking()
        .Where(o => o.ReceiptDocumentKey != null && keys.Contains(o.ReceiptDocumentKey))
        .Select(o => o.ReceiptDocumentKey!)
        .ToListAsync(ct);

    // Ordinal: de esta comparación depende si se borra el fichero de un cliente.
    return referenced.ToHashSet(StringComparer.Ordinal);
  }

  public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
