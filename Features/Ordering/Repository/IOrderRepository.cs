using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Features.Ordering.Repository;


/// <summary>Acceso a las órdenes.</summary>
/// <remarks>
/// No hereda de <c>IBaseRepository&lt;T&gt;</c>: una orden no se actualiza ni se borra —se
/// coloca, y a partir de ahí solo cambia de estado—, así que de las cinco operaciones del
/// CRUD genérico aquí no vale ninguna tal cual.
/// </remarks>
public interface IOrderRepository
{
  /// <summary>Reserva el siguiente número de orden.</summary>
  /// <remarks>
  /// ⚠️ Sale de una <b>secuencia de SQL Server</b> y no de un <c>MAX(Number) + 1</c>: eso
  /// último es un leer-y-escribir y dos compras simultáneas se llevarían el mismo número,
  /// que además tiene índice único — una de las dos reventaría. La secuencia la sirve el
  /// motor, sin bloquear.
  /// </remarks>
  Task<long> NextNumberAsync(CancellationToken ct = default);

  /// <summary>Añade la orden. <b>No hace <c>SaveChanges</c></b>: manda la transacción de negocio.</summary>
  void Add(Order order);

  /// <summary>Una orden con sus líneas, solo si es de ese comprador.</summary>
  /// <remarks>
  /// El filtro por comprador va <b>en la consulta</b> y no en un <c>if</c> posterior: es
  /// la diferencia entre "no existe para ti" y "existe y te digo que no puedes verla".
  /// Lo segundo ya filtra que existe.
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

  Task SaveChangesAsync(CancellationToken ct = default);
}
