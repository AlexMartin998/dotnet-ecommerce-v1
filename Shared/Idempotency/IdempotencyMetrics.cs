using System.Diagnostics.Metrics;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Contador de cómo termina cada petición protegida por <c>Idempotency-Key</c>.
/// </summary>
/// <remarks>
/// <para>
/// Existe por un hallazgo concreto: bajo carga el almacén <b>se apaga solo</b> —el
/// <c>SET</c> agota el timeout de 1000 ms con Redis sano, se degrada en abierto y la
/// petición se ejecuta sin garantía— y la única señal era una línea de <c>Warning</c>
/// que nadie mira. Un fallo silencioso en el mecanismo que existe precisamente para no
/// cobrar dos veces.
/// </para>
/// <para>
/// La dimensión que importa es <c>outcome=unguaranteed</c>: si eso deja de ser cero, la
/// protección contra el doble cobro está desactivada para esa fracción del tráfico,
/// aunque todas las respuestas sean 200.
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

  /// <summary>Se reservó la clave y la acción se ejecutó con garantía.</summary>
  public void Executed() => Count("executed");

  /// <summary>Se reprodujo una respuesta ya memorizada.</summary>
  public void Replayed() => Count("replayed");

  /// <summary>Había otra petición idéntica en curso (409).</summary>
  public void InProgress() => Count("in_progress");

  /// <summary>La clave se reusó con otro cuerpo (422).</summary>
  public void BodyMismatch() => Count("body_mismatch");

  /// <summary>La clave del cliente no era aceptable (400).</summary>
  public void InvalidKey() => Count("invalid_key");

  /// <summary>
  /// ⚠️ La acción se ejecutó <b>sin garantía</b> porque el almacén no contestó.
  /// </summary>
  public void Unguaranteed() => Count("unguaranteed");

  private void Count(string outcome) => _requests.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
}
