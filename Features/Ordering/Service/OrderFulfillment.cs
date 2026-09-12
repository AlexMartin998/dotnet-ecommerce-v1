using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>El camino que puede recorrer una orden después de cobrarse.</summary>
/// <remarks>
/// Cancelar y reembolsar NO están aquí: cancelar una orden pagada exige devolver el dinero,
/// y cancelar una sin pagar ya tiene dueño (<c>AbandonedOrderCleaner</c>, que devuelve el
/// stock en la misma transacción).
/// </remarks>
public static class OrderFulfillment
{
  /// <summary>De qué estado tiene que venir cada destino. Un destino sin entrada no es legal.</summary>
  private static readonly IReadOnlyDictionary<OrderStatus, OrderStatus> Sources =
      new Dictionary<OrderStatus, OrderStatus>
      {
        [OrderStatus.Preparing] = OrderStatus.Paid,
        [OrderStatus.Shipped] = OrderStatus.Preparing,
        [OrderStatus.Delivered] = OrderStatus.Shipped
      };

  /// <summary>Los destinos válidos, en minúsculas, para el mensaje de error y la documentación.</summary>
  public static IReadOnlyCollection<string> Targets { get; } =
      [.. Sources.Keys.Select(s => s.ToString().ToLowerInvariant())];

  /// <summary>Traduce el destino que pide el cliente y dice desde dónde se llega.</summary>
  /// <returns><c>false</c> si ese nombre no es un destino alcanzable por un administrador.</returns>
  public static bool TryResolve(string target, out OrderStatus to, out OrderStatus from)
  {
    from = default;

    return Enum.TryParse(target, ignoreCase: true, out to) && Sources.TryGetValue(to, out from);
  }
}
