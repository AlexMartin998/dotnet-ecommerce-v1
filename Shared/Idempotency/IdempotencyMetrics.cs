using System.Diagnostics.Metrics;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Contador de cómo se resuelve la puerta de admisión de <c>Idempotency-Key</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nació de un hallazgo: bajo carga el almacén <b>se apagaba solo</b> —el <c>SET</c>
/// agotaba el timeout de 1000 ms con Redis sano— y la única señal era un <c>Warning</c>
/// que nadie mira. Entonces eso significaba ejecutar sin garantía; hoy, con la garantía
/// en la transacción, significa solo que se perdió el atajo.
/// </para>
/// <para>
/// La dimensión a vigilar es <c>outcome=gate_unavailable</c>. Ya no es un problema de
/// corrección, pero sí un aviso temprano: sin puerta, las tormentas de reintentos pasan
/// enteras a SQL y se resuelven bloqueándose en la clave primaria, o sea consumiendo
/// conexiones.
/// </para>
/// </remarks>
public sealed class IdempotencyMetrics
{
  public const string MeterName = "ApiEcommerce.Idempotency";

  private readonly Counter<long> _requests;

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
