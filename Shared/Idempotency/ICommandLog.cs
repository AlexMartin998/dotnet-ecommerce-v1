namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Registro de comandos ya ejecutados. Es el puerto que hace que «este intento no se
/// ejecuta dos veces» sea una invariante del <b>servicio</b>, y no algo que dependa de
/// que el controller lleve puesto un atributo.
/// </summary>
/// <remarks>
/// <para>
/// Es el gemelo de <see cref="Shared.Messaging.IEventOutbox"/> y se usa igual: se
/// escribe <b>dentro</b> de la transacción de negocio y <b>no hace <c>SaveChanges</c></b>.
/// Quien manda es la transacción del servicio, y la marca debe confirmarse con el efecto
/// o no confirmarse en absoluto.
/// </para>
/// <para>
/// <b>Por qué en la base y no en Redis.</b> La idempotencia con Redis vive <i>fuera</i>
/// de la transacción, y eso deja dos ventanas que ninguna cantidad de código cierra:
/// el proceso puede morir entre el commit y el guardado de la marca (la compra ocurrió y
/// nadie lo recuerda), y el almacén puede no responder justo cuando hay carga, que es
/// cuando el cliente reintenta. Medido: 174 de 14 400 peticiones se ejecutaron sin
/// garantía con Redis <b>sano</b>. Dentro de la transacción esas ventanas no existen,
/// porque la marca y el efecto son la misma escritura.
/// </para>
/// <para>
/// Redis sigue delante como <b>atajo</b> (<c>[Idempotent]</c>): responde antes y evita
/// que una tormenta de duplicados se apile sobre la misma fila. Pero ya no es la
/// garantía, así que degradar en abierto ahí dejó de significar «doble ejecución» y pasa
/// a significar «el atajo no estaba».
/// </para>
/// </remarks>
public interface ICommandLog
{
  /// <summary>
  /// Busca el resultado de un intento ya ejecutado.
  /// </summary>
  /// <remarks>
  /// Devuelve <c>null</c> si es la primera vez. <b>No lanza</b> si la intención no está
  /// declarada (<see cref="CommandIntent.None"/>): en ese caso simplemente no hay nada
  /// que deduplicar.
  /// </remarks>
  /// <typeparam name="TResult">Tipo del resultado que devuelve la operación.</typeparam>
  /// <param name="intent">La intención del comando.</param>
  /// <param name="command">
  /// El comando en sí. La huella se calcula <b>aquí dentro</b>: quien llama no tiene por
  /// qué saber cómo se compara una petición con otra, y así el servicio no acaba
  /// hablando de hashes.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  /// <exception cref="Exceptions.IdempotencyConflictAppException">
  /// La intención ya se usó con un comando <b>distinto</b>. Reproducir el resultado del
  /// otro sería mentirle a quien llama sobre lo que pidió.
  /// </exception>
  Task<TResult?> FindResultAsync<TResult>(
      CommandIntent intent, object command, CancellationToken ct = default) where TResult : class;

  /// <summary>
  /// Deja constancia de que este intento se ejecutó, con su resultado.
  /// </summary>
  /// <remarks>
  /// <b>No hace <c>SaveChanges</c></b> a propósito: lo confirma la transacción de negocio.
  /// Si la intención no está declarada, no hace nada.
  /// </remarks>
  /// <typeparam name="TResult">Tipo del resultado que devuelve la operación.</typeparam>
  /// <param name="intent">La intención del comando.</param>
  /// <param name="command">El comando que se ejecutó.</param>
  /// <param name="result">El resultado a recordar.</param>
  void Record<TResult>(CommandIntent intent, object command, TResult result) where TResult : class;

  /// <summary>
  /// ¿Es este el choque de clave primaria de <see cref="ExecutedCommand"/>, o sea otra
  /// réplica que ejecutó el mismo intento a la vez?
  /// </summary>
  /// <remarks>
  /// ⚠️ Hay que mirar el <b>número de error</b> y no conformarse con el tipo. Un
  /// <c>catch (DbUpdateException)</c> a secas se tragaría también timeouts y deadlocks,
  /// y los daría por «duplicado ignorado» — el mismo fallo que ya se corrigió en
  /// <c>ProductPurchasedConsumer</c>.
  /// </remarks>
  /// <param name="exception">La excepción que lanzó el <c>SaveChanges</c>.</param>
  bool IsDuplicateIntent(Exception exception);
}
