namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Ejecuta un comando exactamente una vez: la marca del intento y su efecto se confirman
/// juntos, o no se confirma ninguno de los dos.
/// </summary>
/// <remarks>
/// Gemelo de <see cref="Shared.Messaging.IMessageInbox"/>, que hace lo mismo para los
/// mensajes entrantes. Es una pieza propia porque la envoltura es la GARANTÍA, no una
/// conveniencia: copiada en cada caso de uso, basta que el tercero olvide el
/// <c>catch</c> del choque de clave para que un duplicado salga como 500.
/// </remarks>
public interface IIdempotentCommandRunner
{
  /// <summary>
  /// Ejecuta <paramref name="effect"/> si este intento no se había ejecutado ya.
  /// </summary>
  /// <remarks>
  /// El efecto NO debe hacer el <c>SaveChanges</c> final: lo hace el runner junto a la
  /// marca. Puede hacer los intermedios que necesite (por ejemplo para obtener un id
  /// generado), que van en la misma transacción. Debe ser replayable, porque la
  /// transacción puede reintentarse ante un fallo transitorio.
  /// </remarks>
  /// <typeparam name="TResult">Lo que devuelve la operación, y lo que se recuerda.</typeparam>
  /// <param name="intent">La intención declarada por el cliente.</param>
  /// <param name="command">El comando en sí, del que sale la huella del cuerpo.</param>
  /// <param name="effect">Qué hacer. Se ejecuta dentro de la transacción.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <exception cref="Exceptions.ConflictAppException">
  /// Otra réplica está ejecutando el mismo intento y aún no ha confirmado.
  /// </exception>
  /// <exception cref="Exceptions.IdempotencyConflictAppException">
  /// La intención ya se usó con un comando distinto.
  /// </exception>
  Task<CommandOutcome<TResult>> RunAsync<TResult>(
      CommandIntent intent, object command,
      Func<CancellationToken, Task<TResult>> effect,
      CancellationToken ct = default) where TResult : class;
}
