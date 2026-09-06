using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Features.Ordering.Repository;


/// <summary>Acceso a las órdenes.</summary>
/// <remarks>
/// No hereda de <c>IBaseRepository&lt;T&gt;</c>: una orden se coloca y luego solo cambia de
/// estado, así que ninguna de las cinco operaciones del CRUD genérico vale tal cual.
/// </remarks>
public interface IOrderRepository
{
  /// <summary>Reserva el siguiente número de orden.</summary>
  /// <remarks>
  /// Sale de una secuencia de SQL Server y no de un <c>MAX(Number) + 1</c>: eso es
  /// leer-y-escribir, y dos compras simultáneas chocarían contra el índice único.
  /// </remarks>
  Task<long> NextNumberAsync(CancellationToken ct = default);

  /// <summary>Añade la orden. No hace <c>SaveChanges</c>: manda la transacción de negocio.</summary>
  void Add(Order order);

  /// <summary>Una orden con sus líneas, solo si es de ese comprador.</summary>
  /// <remarks>
  /// El filtro por comprador va en la consulta y no en un <c>if</c> posterior: "existe pero
  /// no puedes verla" ya filtra que existe.
  /// </remarks>
  Task<Order?> FindForBuyerAsync(int id, string buyerUserId, CancellationToken ct = default);

  /// <summary>Una orden con sus líneas, sin filtrar por comprador. Para el generador.</summary>
  Task<Order?> FindWithItemsAsync(int id, CancellationToken ct = default);

  /// <summary>Órdenes de un comprador, de la más reciente a la más antigua.</summary>
  Task<(IReadOnlyList<Order> Items, int Total)> GetPagedForBuyerAsync(
      string buyerUserId, int skip, int take, CancellationToken ct = default);

  /// <summary>Deja constancia de que el comprobante ya está disponible.</summary>
  Task SetReceiptAsync(int orderId, string documentKey, CancellationToken ct = default);

  /// <summary>Marca que la generación del comprobante falló.</summary>
  Task SetReceiptFailedAsync(int orderId, CancellationToken ct = default);

  /// <summary>De las claves dadas, cuáles referencia alguna orden.</summary>
  /// <remarks>
  /// Por lotes y no fichero a fichero: el recolector recorre el almacén entero.
  /// </remarks>
  Task<IReadOnlySet<string>> FindReferencedKeysAsync(
      IReadOnlyCollection<string> keys, CancellationToken ct = default);

  Task SaveChangesAsync(CancellationToken ct = default);
}
