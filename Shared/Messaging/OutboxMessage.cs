using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Evento de dominio pendiente de publicar, guardado en la misma transacción que el cambio de
/// negocio que lo produjo (patrón outbox transaccional).
/// </summary>
/// <remarks>
/// Base de datos y broker no se pueden escribir atómicamente; como fila, o pasan las dos cosas
/// o no pasa ninguna, y la API sigue aceptando compras con el broker caído. La garantía es
/// at-least-once, así que el consumidor debe deduplicar (ver <see cref="ProcessedMessage"/>).
/// </remarks>
public class OutboxMessage
{
  /// <summary>Id del mensaje. Viaja al broker y es la clave que usa el consumidor para deduplicar.</summary>
  [Key]
  public Guid Id { get; set; } = Guid.NewGuid();

  /// <summary>Nombre del evento (<c>product.purchased</c>). Es también la routing key.</summary>
  [Required]
  [MaxLength(100)]
  public required string Type { get; set; }

  /// <summary>Cuerpo del evento serializado en JSON.</summary>
  [Required]
  public required string Payload { get; set; }

  /// <summary>Orden de inserción, asignado por la base (columna <c>IDENTITY</c>).</summary>
  /// <remarks>
  /// El publicador ordena por esto y no por <see cref="OccurredAt"/>, que depende del reloj de
  /// cada réplica. No garantiza el orden de publicación —el IDENTITY se asigna al INSERT y la
  /// fila se ve al COMMIT—, y se deja así porque ningún consumidor exige orden.
  /// </remarks>
  public long Sequence { get; set; }

  /// <summary>Cuándo ocurrió el hecho de negocio. Informativo: el ORDEN lo da <see cref="Sequence"/>.</summary>
  public DateTime OccurredAt { get; set; } = DateTime.Now;

  /// <summary>Momento en que se publicó con éxito. <c>null</c> = pendiente.</summary>
  public DateTime? ProcessedAt { get; set; }

  /// <summary>Intentos de publicación fallidos. Sirve para el backoff y para detectar mensajes envenenados.</summary>
  public int Attempts { get; set; }

  [MaxLength(1000)]
  public string? LastError { get; set; }
}


/// <summary>Mensajes ya consumidos, para que reprocesar uno no repita su efecto.</summary>
/// <remarks>
/// Es la otra mitad del at-least-once: el broker va a reentregar mensajes (reinicio del
/// consumidor, nack, publicación duplicada por la outbox).
/// </remarks>
public class ProcessedMessage
{
  /// <summary>Id del mensaje consumido. Clave primaria: el propio índice impide el duplicado.</summary>
  [Key]
  public Guid Id { get; set; }

  [Required]
  [MaxLength(100)]
  public required string Type { get; set; }

  public DateTime ProcessedAt { get; set; } = DateTime.Now;
}
