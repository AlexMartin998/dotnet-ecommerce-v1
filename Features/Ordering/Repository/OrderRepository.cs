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

  public async Task<(IReadOnlyList<Order> Items, int Total)> GetPagedAsync(
      string? numberPrefix, int skip, int take, CancellationToken ct = default)
  {
    var query = db.Orders.AsNoTracking();

    if (!string.IsNullOrWhiteSpace(numberPrefix))
    {
      var prefix = numberPrefix.Trim();

      query = query.Where(o => o.Number.StartsWith(prefix));
    }

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

  public async Task<bool> TryMarkPaidAsync(int orderId, CancellationToken ct = default)
      => await TryTransitionAsync(orderId, OrderStatus.Placed, OrderStatus.Paid, ct);

  public async Task<bool> TryCancelAsync(int orderId, CancellationToken ct = default)
      => await TryTransitionAsync(orderId, OrderStatus.Placed, OrderStatus.Cancelled, ct);

  public async Task<IReadOnlyDictionary<OrderStatus, int>> CountByStatusAsync(
      CancellationToken ct = default)
      => await db.Orders
          .AsNoTracking()
          .GroupBy(o => o.Status)
          .Select(g => new { Status = g.Key, Count = g.Count() })
          .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

  public async Task<OrderStatus?> FindStatusAsync(int orderId, CancellationToken ct = default)
      => await db.Orders
          .AsNoTracking()
          .Where(o => o.Id == orderId)
          .Select(o => (OrderStatus?)o.Status)
          .FirstOrDefaultAsync(ct);

  /// <summary>Mueve la orden en una sola sentencia.</summary>
  /// <remarks>
  /// La condición va DENTRO del UPDATE: leer el estado y escribir después dejaría hueco a
  /// que un webhook y el recolector movieran la misma orden a la vez.
  /// </remarks>
  public async Task<bool> TryTransitionAsync(
      int orderId, OrderStatus from, OrderStatus target, CancellationToken ct = default)
  {
    // Fuera del árbol de expresión: dentro, DateTime.Now se traduciría a GETDATE().
    var now = DateTime.Now;

    var affected = await db.Orders
        .Where(o => o.Id == orderId && o.Status == from)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.Status, target)
            .SetProperty(o => o.UpdatedAt, now), ct);

    return affected == 1;
  }

  public async Task<IReadOnlyList<Order>> FindAwaitingPaymentBeforeAsync(
      DateTime cutoff, int limit, CancellationToken ct = default)
      => await db.Orders
          .AsNoTracking()
          .Where(o => o.Status == OrderStatus.Placed && o.PlacedAt < cutoff)
          .OrderBy(o => o.PlacedAt)
          .Take(limit)
          .Include(o => o.Items)
          .ToListAsync(ct);

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
