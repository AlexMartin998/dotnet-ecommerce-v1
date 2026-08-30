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

  /// <summary>Cola que consume este servicio.</summary>
  [Required]
  public string Queue { get; init; } = "apiecommerce.product-purchased";

  /// <summary>Patrón de routing keys al que se suscribe la cola.</summary>
  [Required]
  public string RoutingKey { get; init; } = "product.purchased";

  /// <summary>
  /// Mensajes sin confirmar que el broker entrega a la vez (QoS).
  /// Con prefetch alto un consumidor acapara mensajes que otro podría procesar.
  /// </summary>
  [Range(1, 1000)]
  public ushort PrefetchCount { get; init; } = 10;

  /// <summary>Cada cuánto drena el publicador la tabla outbox.</summary>
  [Range(1, 300)]
  public int PublishIntervalSeconds { get; init; } = 5;

  public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

  public string DeadLetterExchange => $"{Exchange}.dlx";
  public string DeadLetterQueue => $"{Queue}.dlq";
}
