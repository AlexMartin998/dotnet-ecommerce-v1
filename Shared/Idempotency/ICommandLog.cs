namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Registro de comandos ya ejecutados, para que «este intento no se ejecuta dos veces»
/// sea una invariante del servicio y no del controller.
/// </summary>
/// <remarks>
/// Gemelo de <see cref="Shared.Messaging.IEventOutbox"/>: se escribe dentro de la
/// transacción de negocio y no hace <c>SaveChanges</c>, para que la marca y el efecto se
/// confirmen juntos. Redis queda delante solo como atajo, no como garantía.
/// </remarks>
public interface ICommandLog
{
  /// <summary>
  /// Busca el resultado de un intento ya ejecutado; <c>null</c> si es la primera vez.
  /// </summary>
  /// <remarks>
  /// No lanza si la intención no está declarada (<see cref="CommandIntent.None"/>).
  /// </remarks>
  /// <typeparam name="TResult">Tipo del resultado que devuelve la operación.</typeparam>
  /// <param name="intent">La intención del comando.</param>
  /// <param name="command">
  /// El comando en sí. La huella se calcula aquí dentro para que el servicio no tenga que
  /// hablar de hashes.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  /// <exception cref="Exceptions.IdempotencyConflictAppException">
  /// La intención ya se usó con un comando distinto.
  /// </exception>
  Task<TResult?> FindResultAsync<TResult>(
      CommandIntent intent, object command, CancellationToken ct = default) where TResult : class;

  /// <summary>
  /// Deja constancia de que este intento se ejecutó, con su resultado.
  /// </summary>
  /// <remarks>
  /// No hace <c>SaveChanges</c>: lo confirma la transacción de negocio. Si la intención no
  /// está declarada, no hace nada.
  /// </remarks>
  /// <typeparam name="TResult">Tipo del resultado que devuelve la operación.</typeparam>
  /// <param name="intent">La intención del comando.</param>
  /// <param name="command">El comando que se ejecutó.</param>
  /// <param name="result">El resultado a recordar.</param>
  void Record<TResult>(CommandIntent intent, object command, TResult result) where TResult : class;

  /// <summary>
  /// Indica si la excepción es el choque de clave primaria de
  /// <see cref="ExecutedCommand"/>, es decir otra réplica ejecutando el mismo intento.
  /// </summary>
  /// <remarks>
  /// Mira el número de error, no el tipo: un <c>catch (DbUpdateException)</c> a secas daría
  /// también timeouts y deadlocks por «duplicado ignorado».
  /// </remarks>
  /// <param name="exception">La excepción que lanzó el <c>SaveChanges</c>.</param>
  bool IsDuplicateIntent(Exception exception);
}
