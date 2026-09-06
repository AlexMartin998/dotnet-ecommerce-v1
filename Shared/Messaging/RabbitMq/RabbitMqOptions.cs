using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>Configuración del broker (sección <c>RabbitMq</c>).</summary>
public sealed class RabbitMqOptions
{
  public const string SectionName = "RabbitMq";

  /// <summary>
  /// <c>amqp://user:pass@host:5672</c>. <b>Vacío = mensajería desactivada</b>: la API
  /// arranca igual y los eventos se acumulan en el outbox.
  /// </summary>
  public string ConnectionString { get; init; } = string.Empty;

  /// <summary>Exchange de tipo <c>topic</c> donde se publican los eventos de dominio.</summary>
  [Required]
  public string Exchange { get; init; } = "apiecommerce.events";

  /// <summary>Cola que consume el <b>catálogo</b>.</summary>
  /// <remarks>
  /// Sigue aquí solo por compatibilidad con los despliegues que ya la traen. Un consumidor nuevo
  /// declara su <see cref="EventSubscription"/> en su slice y se la pasa a <c>AddEventConsumer</c>.
  /// </remarks>
  [Required]
  public string Queue { get; init; } = "apiecommerce.product-purchased";

  /// <summary>Patrón de routing keys al que se suscribe la cola del catálogo.</summary>
  [Required]
  public string RoutingKey { get; init; } = "product.purchased";

  /// <summary>
  /// Mensajes sin confirmar que el broker entrega a la vez (QoS).
  /// Con prefetch alto un consumidor acapara mensajes que otro podría procesar.
  /// </summary>
  [Range(1, 1000)]
  public ushort PrefetchCount { get; init; } = 10;

  // PublishIntervalSeconds, MaxPublishAttempts y BatchSize viven en OutboxOptions: son del outbox,
  // que es agnóstico al broker.

  /// <summary>Entregas antes de mandar un mensaje a la DLQ.</summary>
  /// <remarks>
  /// Lo cuenta la cabecera propia <see cref="RetryAttempts"/>, no una bandera del broker como
  /// <c>args.Redelivered</c>, que no es un contador.
  /// </remarks>
  [Range(1, 20)]
  public int MaxDeliveryAttempts { get; init; } = 5;

  /// <summary>Espera antes de reintentar un mensaje fallido (TTL de la cola de reintento).</summary>
  /// <remarks>
  /// Sin espera, los reintentos caen dentro del mismo incidente y se agotan antes de que nada se
  /// haya recuperado.
  /// </remarks>
  [Range(1, 3600)]
  public int RetryDelaySeconds { get; init; } = 30;

  /// <summary>Hay broker configurado.</summary>
  public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

  /// <summary>Dead-letter <b>heredada</b> del catálogo.</summary>
  /// <remarks>
  /// No se generaliza: las colas ya declaradas la llevan en su <c>x-dead-letter-exchange</c> y
  /// redeclararlas con otro valor da 406. Los slices nuevos usan <see cref="EventSubscription.For"/>.
  /// </remarks>
  public string DeadLetterExchange => $"{Exchange}.dlx";

  // DeadLetterQueue y RetryQueue viven en EventSubscription: son de UNA cola, no del broker.
}
