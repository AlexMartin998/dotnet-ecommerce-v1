using System.Diagnostics.Metrics;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Contador de cómo se resuelve la puerta de admisión de <c>Idempotency-Key</c>.
/// </summary>
/// <remarks>
/// La dimensión a vigilar es <c>outcome=gate_unavailable</c>: no es un problema de
/// corrección, pero sin puerta las tormentas de reintentos pasan enteras a SQL y se
/// resuelven bloqueándose en la clave primaria, consumiendo conexiones.
/// </remarks>
public sealed class IdempotencyMetrics
{
  /// <summary>Nombre del <see cref="Meter"/>.</summary>
  public const string MeterName = "ApiEcommerce.Idempotency";

  private readonly Counter<long> _requests;

  /// <summary>Crea el contador sobre el <see cref="IMeterFactory"/> de la aplicación.</summary>
  public IdempotencyMetrics(IMeterFactory factory)
  {
    var meter = factory.Create(MeterName);

    _requests = meter.CreateCounter<long>(
        "apiecommerce.idempotency.requests",
        unit: "{request}",
        description: "Peticiones con Idempotency-Key, por cómo se resolvieron.");
  }

  /// <summary>Había otra petición idéntica en vuelo (409).</summary>
  public void InProgress() => Count("in_progress");

  /// <summary>La clave del cliente no era aceptable (400).</summary>
  public void InvalidKey() => Count("invalid_key");

  /// <summary>
  /// La puerta no contestó. La petición sigue: la garantía no depende de ella.
  /// </summary>
  public void GateUnavailable() => Count("gate_unavailable");

  private void Count(string outcome) => _requests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
