using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// La <b>intención</b> de un comando: qué operación se pide y con qué identidad de
/// intento, para que dos peticiones que son el mismo intento no se ejecuten dos veces.
/// </summary>
/// <remarks>
/// <para>
/// No es una «Idempotency-Key». Esa es la forma que toma la intención cuando llega
/// <b>por HTTP</b>; un job que reprocesa una cola tiene la suya (el <c>MessageId</c>, el
/// id del pedido) y no manda cabeceras. El servicio de negocio debe hablar de
/// intenciones, no del protocolo por el que llegaron — igual que habla de
/// <c>ITransactionRunner</c> y no de <c>DbContext</c>.
/// </para>
/// <para>
/// Es un parámetro <b>obligatorio</b> de la operación, y eso es deliberado. El bug que
/// motivó mover la transacción del controller al servicio fue exactamente este: la
/// garantía dependía de que alguien se acordara de poner un atributo, y llamar a
/// <c>BuyAsync</c> desde otro sitio la perdía en silencio. Aquí el compilador obliga a
/// decidir, y renunciar a la protección tiene que escribirse: <see cref="None"/>.
/// </para>
/// </remarks>
/// <param name="Operation">
/// Qué operación es, en el lenguaje del dominio (<c>catalog.buy-product</c>). Acota la
/// clave: la misma intención en dos operaciones distintas no debe colisionar.
/// </param>
/// <param name="Key">
/// Identidad del intento, ya acotada a quien lo pide. Vacía = sin protección.
/// </param>
public readonly record struct CommandIntent(string Operation, string Key)
{
  /// <summary>
  /// Renuncia explícita a la deduplicación: la operación se ejecutará siempre.
  /// </summary>
  /// <remarks>
  /// Existe para que «no quiero idempotencia» sea una frase que alguien escribió, y no
  /// un parámetro que se olvidó. Es la diferencia entre una decisión y un descuido.
  /// </remarks>
  public static CommandIntent None { get; } = new(string.Empty, string.Empty);

  public bool IsDeclared => !string.IsNullOrEmpty(Key);

  /// <summary>Clave de almacenamiento, acotada por operación.</summary>
  public string StorageKey => $"{Operation}:{Key}";
}


/// <summary>
/// Comando ya ejecutado, con su resultado. Es la <b>garantía</b> de exactamente-una-vez:
/// se escribe en la MISMA transacción que el efecto.
/// </summary>
/// <remarks>
/// <para>
/// Es la hermana de <c>ProcessedMessage</c>, que hace lo mismo para los mensajes que
/// llegan del broker, y por la misma razón: si la marca y el efecto no se confirman
/// juntos, hay una ventana en la que uno existe sin el otro. En el consumidor esa
/// ventana produjo un P0 —el mensaje se reconocía como duplicado y desaparecía sin
/// procesarse— y se cerró metiendo las dos cosas en una transacción. Por HTTP la ventana
/// era la contraria: la compra se confirmaba, el proceso moría antes de memorizar la
/// respuesta, y el reintento del cliente <b>volvía a comprar</b>.
/// </para>
/// <para>
/// ⚠️ Quien arbitra entre réplicas es la <b>clave primaria</b>, no el código. Si dos
/// instancias ejecutan el mismo intento a la vez, la segunda se queda bloqueada en la
/// clave hasta que la primera confirme, y entonces choca: su transacción entera —marca y
/// efecto— se deshace. No hace falta ni reserva, ni TTL, ni token de propiedad; la base
/// ya sabe hacer esto.
/// </para>
/// </remarks>
public class ExecutedCommand
{
  /// <summary>
  /// <see cref="CommandIntent.StorageKey"/>. Clave primaria: el propio índice impide el duplicado.
  /// </summary>
  /// <remarks>
  /// ⚠️ Con <c>MaxLength</c>, no <c>nvarchar(max)</c>: en SQL Server eso <b>no es
  /// indexable</b> y no podría ser clave primaria. Misma trampa que ya se pisó con el
  /// índice único de <c>SKU</c>.
  /// </remarks>
  [Key]
  [MaxLength(ExecutedCommandLimits.MaxKeyLength)]
  public required string Id { get; set; }

  /// <summary>
  /// Huella del cuerpo con el que se ejecutó, para detectar que se reusó la intención
  /// con otra petición distinta.
  /// </summary>
  [Required]
  [MaxLength(64)]
  public required string RequestHash { get; set; }

  /// <summary>
  /// El resultado que devolvió la operación, serializado, para poder repetirlo.
  /// </summary>
  /// <remarks>
  /// Se guarda el <b>resultado del servicio</b> (el DTO), no la respuesta HTTP. Eso lo
  /// deja fuera del protocolo —el servicio no conoce códigos de estado ni cabeceras— y
  /// de regalo arregla la identidad byte a byte: al repetirlo, el DTO vuelve a pasar por
  /// el mismo formateador de MVC, así que sale idéntico en vez de «equivalente».
  /// </remarks>
  public string? Result { get; set; }

  public DateTime ExecutedAt { get; set; } = DateTime.Now;
}


/// <summary>
/// El resultado de un comando, más el hecho de si <b>ya se había ejecutado</b>.
/// </summary>
/// <remarks>
/// <para>
/// «Esto ya se ejecutó antes» es un hecho del dominio, no un detalle de HTTP: el
/// servicio puede reportarlo sin conocer cabeceras ni códigos de estado, y es el
/// adaptador quien decide traducirlo a <c>Idempotency-Replayed: true</c>.
/// </para>
/// <para>
/// Existe porque, al bajar la garantía a la transacción, el atajo de Redis dejó de ser
/// el único sitio donde se reproduce una respuesta. Sin esto, la cabecera solo aparecía
/// cuando contestaba el atajo, y un cliente no podía distinguir «tu reintento se
/// reprodujo» de «tu reintento se ejecutó»: una cabecera que <i>a veces</i> marca los
/// replays es peor que no tenerla.
/// </para>
/// </remarks>
/// <typeparam name="TResult">Tipo del resultado de la operación.</typeparam>
/// <param name="Result">Lo que devuelve la operación.</param>
/// <param name="WasReplayed">
/// <c>true</c> si el comando ya estaba registrado y esto es su resultado recordado.
/// </param>
public readonly record struct CommandOutcome<TResult>(TResult Result, bool WasReplayed);
