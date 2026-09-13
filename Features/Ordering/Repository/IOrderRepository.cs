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
  Task<Order?> FindForBuyerAsync(Guid publicId, string buyerUserId, CancellationToken ct = default);

  /// <summary>Una orden con sus líneas, sin filtrar por comprador. Para el generador.</summary>
  Task<Order?> FindWithItemsAsync(int id, CancellationToken ct = default);

  /// <summary>Órdenes de un comprador, de la más reciente a la más antigua.</summary>
  Task<(IReadOnlyList<Order> Items, int Total)> GetPagedForBuyerAsync(
      string buyerUserId, int skip, int take, CancellationToken ct = default);

  /// <summary>Órdenes de todos los compradores. Solo para administración.</summary>
  /// <remarks>
  /// <paramref name="numberPrefix"/> filtra por prefijo del número y no por subcadena: un
  /// <c>LIKE '%x%'</c> no puede usar <c>IX_Orders_Number</c> y degrada a recorrido completo.
  /// </remarks>
  Task<(IReadOnlyList<Order> Items, int Total)> GetPagedAsync(
      string? numberPrefix, int skip, int take, CancellationToken ct = default);

  /// <summary>Pasa la orden a pagada, solo si estaba esperando pago.</summary>
  /// <remarks>
  /// Condicional a propósito: que la transición sea <c>Placed -> Paid</c> y no una
  /// asignación es lo que la hace idempotente ante un reenvío del webhook, y lo que impide
  /// resucitar una orden ya cancelada por abandono.
  /// </remarks>
  /// <returns><c>true</c> si esta llamada fue la que la movió.</returns>
  Task<bool> TryMarkPaidAsync(int orderId, CancellationToken ct = default);

  /// <summary>Cancela la orden, solo si seguía esperando pago.</summary>
  /// <returns><c>true</c> si esta llamada fue la que la canceló.</returns>
  Task<bool> TryCancelAsync(int orderId, CancellationToken ct = default);

  /// <summary>Mueve la orden de un estado a otro, y solo desde ese estado.</summary>
  /// <remarks>
  /// Misma forma que las dos de arriba y por la misma razón: la condición va dentro del
  /// UPDATE, así que dos administradores a la vez no pueden saltarse un paso del ciclo.
  /// </remarks>
  /// <returns><c>true</c> si esta llamada fue la que la movió.</returns>
  Task<bool> TryTransitionAsync(
      int orderId, OrderStatus from, OrderStatus to, CancellationToken ct = default);

  /// <summary>Igual que la de arriba, con la orden citada por su identificador público.</summary>
  Task<bool> TryTransitionAsync(
      Guid publicId, OrderStatus from, OrderStatus to, CancellationToken ct = default);

  /// <summary>Cuántas órdenes hay en cada estado, en una sola consulta.</summary>
  /// <remarks>Se cuenta en la base: traer las filas para contarlas no escala con la tabla.</remarks>
  Task<IReadOnlyDictionary<OrderStatus, int>> CountByStatusAsync(CancellationToken ct = default);

  /// <summary>El estado actual, o <c>null</c> si la orden no existe.</summary>
  /// <remarks>
  /// Solo se consulta cuando el UPDATE condicional no movió nada, para distinguir «ya
  /// estaba ahí» de «venía de otro estado». Nunca para decidir antes de escribir.
  /// </remarks>
  Task<OrderStatus?> FindStatusAsync(Guid publicId, CancellationToken ct = default);

  /// <summary>Órdenes que siguen esperando pago desde antes del corte.</summary>
  /// <remarks>Por lotes: el recolector no puede traerse la tabla entera.</remarks>
  Task<IReadOnlyList<Order>> FindAwaitingPaymentBeforeAsync(
      DateTime cutoff, int limit, CancellationToken ct = default);

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
