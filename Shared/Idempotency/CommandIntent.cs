using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// La intención de un comando: qué operación se pide y con qué identidad de intento, para
/// que dos peticiones que son el mismo intento no se ejecuten dos veces.
/// </summary>
/// <remarks>
/// No es una «Idempotency-Key»: esa es la forma que toma la intención cuando llega por
/// HTTP, y un job que reprocesa una cola tiene la suya. Es parámetro obligatorio para que
/// renunciar a la protección haya que escribirlo (<see cref="None"/>).
/// </remarks>
/// <param name="Operation">
/// Qué operación es, en el lenguaje del dominio (<c>catalog.buy-product</c>). Acota la
/// clave para que la misma intención en dos operaciones distintas no colisione.
/// </param>
/// <param name="Key">
/// Identidad del intento, ya acotada a quien lo pide. Vacía = sin protección.
/// </param>
public readonly record struct CommandIntent(string Operation, string Key)
{
  /// <summary>
  /// Renuncia explícita a la deduplicación: la operación se ejecutará siempre.
  /// </summary>
  public static CommandIntent None { get; } = new(string.Empty, string.Empty);

  /// <summary>¿Se declaró una identidad de intento?</summary>
  public bool IsDeclared => !string.IsNullOrEmpty(Key);

  /// <summary>Clave de almacenamiento, acotada por operación.</summary>
  public string StorageKey => $"{Operation}:{Key}";
}


/// <summary>
/// Comando ya ejecutado, con su resultado. Es la garantía de exactamente-una-vez: se
/// escribe en la MISMA transacción que el efecto.
/// </summary>
/// <remarks>
/// Hermana de <c>ProcessedMessage</c> para los mensajes del broker. Entre réplicas arbitra
/// la clave primaria: la segunda espera en la clave y choca, deshaciendo su transacción
/// entera, sin necesidad de reserva, TTL ni token de propiedad.
/// </remarks>
public class ExecutedCommand
{
  /// <summary>
  /// <see cref="CommandIntent.StorageKey"/>. Clave primaria: el propio índice impide el duplicado.
  /// </summary>
  /// <remarks>
  /// Con <c>MaxLength</c>, no <c>nvarchar(max)</c>: en SQL Server eso no es indexable y no
  /// podría ser clave primaria.
  /// </remarks>
  [Key]
  [MaxLength(ExecutedCommandLimits.MaxKeyLength)]
  public required string Id { get; set; }

  /// <summary>
  /// Huella del cuerpo con el que se ejecutó, para detectar que se reusó la intención con
  /// otra petición distinta.
  /// </summary>
  [Required]
  [MaxLength(64)]
  public required string RequestHash { get; set; }

  /// <summary>
  /// El resultado que devolvió la operación, serializado, para poder repetirlo.
  /// </summary>
  /// <remarks>
  /// Se guarda el DTO del servicio, no la respuesta HTTP: al repetirlo vuelve a pasar por
  /// el mismo formateador de MVC, así que sale idéntico byte a byte.
  /// </remarks>
  public string? Result { get; set; }

  /// <summary>Cuándo se ejecutó el comando.</summary>
  public DateTime ExecutedAt { get; set; } = DateTime.Now;
}


/// <summary>
/// El resultado de un comando, más el hecho de si ya se había ejecutado.
/// </summary>
/// <remarks>
/// «Esto ya se ejecutó antes» es un hecho del dominio: el servicio lo reporta sin conocer
/// HTTP y el adaptador lo traduce a <c>Idempotency-Replayed: true</c>.
/// </remarks>
/// <typeparam name="TResult">Tipo del resultado de la operación.</typeparam>
/// <param name="Result">Lo que devuelve la operación.</param>
/// <param name="WasReplayed">
/// <c>true</c> si el comando ya estaba registrado y esto es su resultado recordado.
/// </param>
public readonly record struct CommandOutcome<TResult>(TResult Result, bool WasReplayed);
