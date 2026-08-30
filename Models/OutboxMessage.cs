using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Models;


/// <summary>
/// Evento de dominio pendiente de publicar, guardado en la <b>misma transacción</b>
/// que el cambio de negocio que lo produjo.
/// </summary>
/// <remarks>
/// <para>
/// Esto es el patrón <b>outbox transaccional</b>, y resuelve el problema central de
/// mezclar base de datos y broker: no se puede escribir en los dos atómicamente.
/// Si publicas en RabbitMQ y luego falla el commit, has anunciado una compra que no
/// existe; si haces commit y luego falla la publicación, la compra existe y nadie se
/// entera. Escribiendo el evento como una fila más, <b>o pasan las dos cosas o no
/// pasa ninguna</b>, y un proceso aparte se encarga de publicarlo después.
/// </para>
/// <para>
/// Consecuencia práctica en este repo: <b>la API funciona con RabbitMQ caído</b>.
/// Las compras se completan y los eventos se acumulan aquí hasta que el broker vuelve.
/// </para>
/// <para>
/// Garantía resultante: <b>at-least-once</b>. Si el publicador muere entre publicar y
/// marcar como procesado, el mensaje sale dos veces — por eso el consumidor tiene que
/// ser idempotente (ver <see cref="ProcessedMessage"/>).
/// </para>
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

  public DateTime OccurredAt { get; set; } = DateTime.Now;

  /// <summary>Momento en que se publicó con éxito. <c>null</c> = pendiente.</summary>
  public DateTime? ProcessedAt { get; set; }

  /// <summary>Intentos de publicación fallidos. Sirve para el backoff y para detectar mensajes envenenados.</summary>
  public int Attempts { get; set; }

  [MaxLength(1000)]
  public string? LastError { get; set; }
}


/// <summary>
/// Mensajes ya consumidos, para que reprocesar uno no repita su efecto.
/// </summary>
/// <remarks>
/// Es la otra mitad del at-least-once: el broker <b>va a</b> reentregar mensajes
/// (reinicio del consumidor, nack, publicación duplicada por la outbox). Un consumidor
/// que no deduplica acaba aplicando el mismo efecto dos veces, que es exactamente el
/// bug que se intentaba evitar con la mensajería.
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
