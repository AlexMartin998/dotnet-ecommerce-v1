using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>Comportamiento del outbox (sección <c>Outbox</c>).</summary>
/// <remarks>
/// <b>Sección propia y no dentro de <c>RabbitMq</c>.</b> El outbox es agnóstico al
/// broker: escribe filas en la misma transacción de negocio y las drena a través de
/// <c>IEventPublisher</c>. Tener sus mandos bajo <c>RabbitMq:</c> daba a entender lo
/// contrario, y el día que se cambie de transporte habría que mover configuración que
/// no tenía nada que ver con él.
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
  /// Intentos antes de dar por perdido un mensaje CONCRETO y dejarlo para revisión
  /// manual. <b>Un broker caído no consume intentos</b>: solo los cuentan los fallos
  /// atribuibles al propio mensaje.
  /// </summary>
  /// <remarks>
  /// Lo leen el publicador y la sonda <c>outbox-backlog</c>. Vive aquí para que sea
  /// <b>una sola fuente</b>: cuando eran dos <c>const</c> separadas, subir el máximo en
  /// uno dejaba al otro contando como perdidos mensajes que aún se reintentaban.
  /// </remarks>
  [Range(1, 50)]
  public int MaxPublishAttempts { get; init; } = 5;

  /// <summary>
  /// Días que se conservan las filas ya procesadas antes de purgarlas.
  /// </summary>
  /// <remarks>
  /// No es un detalle de limpieza: <c>OutboxMessages</c> y <c>ProcessedMessages</c>
  /// crecen <b>con cada compra y para siempre</b>. Sin purga, el índice de pendientes se
  /// mantiene barato (está filtrado) pero la tabla no, y las copias de seguridad y el
  /// disco crecen sin techo. Se conserva una ventana porque son la evidencia de qué se
  /// publicó cuando alguien pregunta.
  /// </remarks>
  [Range(1, 3650)]
  public int RetentionDays { get; init; } = 14;

  /// <summary>Cada cuánto pasa el recolector.</summary>
  [Range(1, 168)]
  public int CleanupIntervalHours { get; init; } = 6;

  /// <summary>
  /// Nombre del bloqueo aplicativo que serializa el drenaje entre réplicas.
  /// </summary>
  public string PublisherLockName { get; init; } = "apiecommerce:outbox-publisher";
}
