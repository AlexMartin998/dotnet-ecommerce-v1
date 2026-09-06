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

  // PublishIntervalSeconds, MaxPublishAttempts y BatchSize se movieron a OutboxOptions:
  // son del OUTBOX, que es agnóstico al broker, no de RabbitMQ. Ver Shared/Messaging/OutboxOptions.cs.

  /// <summary>
  /// Entregas antes de mandar un mensaje a la DLQ. Cuenta <b>de verdad</b>: se lee de la
  /// cabecera <c>x-death</c> que escribe el broker al expirar el TTL de la cola de
  /// reintento.
  /// </summary>
  /// <remarks>
  /// Esta opción existió y <b>se quitó</b> porque documentaba algo que no ocurría: el
  /// consumidor miraba <c>args.Redelivered</c>, que es una <i>bandera</i> del broker y no
  /// un contador. Efectivamente eran 2 intentos y con 0 ms entre ellos, porque un
  /// <c>requeue</c> devuelve el mensaje a la <b>cabeza</b> de la cola. Vuelve ahora que
  /// hay un contador real detrás.
  /// </remarks>
  [Range(1, 20)]
  public int MaxDeliveryAttempts { get; init; } = 5;

  /// <summary>Espera antes de reintentar un mensaje fallido (TTL de la cola de reintento).</summary>
  /// <remarks>
  /// Sin espera, reintentar no arregla nada: si el fallo es un timeout de la base o un
  /// servicio saturado, los tres reintentos caen dentro del mismo incidente y se agotan
  /// antes de que nada se haya recuperado.
  /// </remarks>
  [Range(1, 3600)]
  public int RetryDelaySeconds { get; init; } = 30;

  public bool IsEnabled => !string.IsNullOrWhiteSpace(ConnectionString);

  public string DeadLetterExchange => $"{Exchange}.dlx";
  public string DeadLetterQueue => $"{Queue}.dlq";

  /// <summary>Exchange por el que pasan los mensajes que esperan un reintento.</summary>
  public string RetryExchange => $"{Exchange}.retry";

  /// <summary>
  /// Cola de espera: no la consume nadie. Su único trabajo es <b>caducar</b> los mensajes
  /// para que el broker los devuelva a la cola principal.
  /// </summary>
  public string RetryQueue => $"{Queue}.retry";
}
