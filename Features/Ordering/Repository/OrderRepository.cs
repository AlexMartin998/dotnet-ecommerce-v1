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
    // `SELECT NEXT VALUE FOR` es atómico y no bloquea: el motor reparte números sin que
    // dos transacciones puedan llevarse el mismo. Un MAX()+1 sí se los llevaría.
    //
    // ⚠️ La secuencia NO se reinicia por año. El año del número sale de la fecha, así que
    // en 2027 los números seguirán subiendo (ORD-2027-000431). Reiniciarla obligaría a un
    // trabajo anual que alguien olvidaría, y a que el índice único dejara de bastar.
    //
    // ⚠️ Y LOS NÚMEROS PUEDEN TENER HUECOS. Una secuencia no se deshace con el rollback
    // —es su forma de no bloquear—, así que una compra que falla, o un reintento de la
    // estrategia de EF, se lleva un número que ya no usará nadie. Para un comprobante
    // interno da igual. ⚠️ Para una FACTURA no: varias legislaciones exigen numeración
    // correlativa SIN huecos, y eso no se arregla con una secuencia — hace falta una tabla
    // de contadores por serie, bloqueada dentro de la misma transacción, pagando la
    // serialización. El día que esto emita facturas de verdad, esa es la decisión.
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
        .ThenByDescending(o => o.Id)   // desempate estable: sin él, dos órdenes del mismo
                                       // instante pueden salir dos veces o ninguna al paginar
        .Skip(skip)
        .Take(take)
        .Include(o => o.Items)
        .ToListAsync(ct);

    return (items, total);
  }

  public async Task SetReceiptAsync(int orderId, string documentKey, CancellationToken ct = default)
  {
    // El instante se captura FUERA del árbol de expresión: dentro, DateTime.Now se traduce
    // a GETDATE() y lo evaluaría el reloj del servidor SQL.
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
        // ⚠️ Solo si NO tiene comprobante. El aviso de "agotado" llega desde el consumidor
        // y podría cruzarse con una generación que sí funcionó (un replay desde la DLQ que
        // termina bien mientras el mensaje original agota sus intentos): sin esta condición
        // marcaría como fallido un comprobante que ya está en el almacén y descargándose.
        .Where(o => o.Id == orderId && o.ReceiptDocumentKey == null)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.ReceiptStatus, ReceiptStatus.Failed)
            .SetProperty(o => o.UpdatedAt, now), ct);
  }

  public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
