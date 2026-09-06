using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>Comportamiento del outbox (sección <c>Outbox</c>).</summary>
/// <remarks>
/// Sección propia y no dentro de <c>RabbitMq</c>: el outbox es agnóstico al broker, así que
/// sus mandos no deben moverse el día que se cambie de transporte.
/// </remarks>
public sealed class OutboxOptions
{
  public const string SectionName = "Outbox";

  /// <summary>Cada cuánto drena el publicador la tabla.</summary>
  [Range(1, 300)]
  public int PublishIntervalSeconds { get; init; } = 5;

  /// <summary>Mensajes por vuelta. Acota cuánto dura la vuelta y el bloqueo que la protege.</summary>
  [Range(1, 1000)]
  public int BatchSize { get; init; } = 50;

  /// <summary>
  /// Intentos antes de dar por perdido un mensaje concreto y dejarlo para revisión manual.
  /// Un broker caído no consume intentos: solo los fallos atribuibles al propio mensaje.
  /// </summary>
  /// <remarks>
  /// Fuente única del límite: lo leen el publicador y la sonda <c>outbox-backlog</c>.
  /// </remarks>
  [Range(1, 50)]
  public int MaxPublishAttempts { get; init; } = 5;

  /// <summary>Días que se conservan las filas ya procesadas antes de purgarlas.</summary>
  /// <remarks>
  /// <c>OutboxMessages</c> y <c>ProcessedMessages</c> crecen con cada compra y sin techo. Se
  /// conserva una ventana porque son la evidencia de qué se publicó.
  /// </remarks>
  [Range(1, 3650)]
  public int RetentionDays { get; init; } = 14;

  /// <summary>Cada cuánto pasa el recolector.</summary>
  [Range(1, 168)]
  public int CleanupIntervalHours { get; init; } = 6;

  /// <summary>Nombre del bloqueo aplicativo que serializa el drenaje entre réplicas.</summary>
  public string PublisherLockName { get; init; } = "apiecommerce:outbox-publisher";
}
