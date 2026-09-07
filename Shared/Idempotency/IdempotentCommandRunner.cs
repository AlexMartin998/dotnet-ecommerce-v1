using ApiEcommerce.Data;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Db;

namespace ApiEcommerce.Shared.Idempotency;


/// <inheritdoc cref="IIdempotentCommandRunner"/>
public sealed class IdempotentCommandRunner(
    AppDbContext db, ITransactionRunner transactions, ICommandLog commands)
    : IIdempotentCommandRunner
{
  public async Task<CommandOutcome<TResult>> RunAsync<TResult>(
      CommandIntent intent, object command,
      Func<CancellationToken, Task<TResult>> effect,
      CancellationToken ct = default) where TResult : class
  {
    ArgumentNullException.ThrowIfNull(effect);

    try
    {
      return await transactions.ExecuteAsync(async token =>
      {
        // Dentro de la transacción: sin esto habría una ventana en la que el comando se
        // ejecuta y nadie lo recuerda.
        if (await commands.FindResultAsync<TResult>(intent, command, token) is { } already)
          return new CommandOutcome<TResult>(already, WasReplayed: true);

        var result = await effect(token);

        commands.Record(intent, command, result);

        // El choque de clave primaria de ExecutedCommands sale aquí, y arrastra al efecto.
        await db.SaveChangesAsync(token);

        return new CommandOutcome<TResult>(result, WasReplayed: false);
      }, ct);
    }
    catch (Exception ex) when (commands.IsDuplicateIntent(ex))
    {
      // Otra réplica confirmó primero: nuestra transacción se deshizo entera, así que no
      // hay nada que compensar y basta con devolver su resultado.
      var winner = await commands.FindResultAsync<TResult>(intent, command, ct)
          ?? throw new ConflictAppException(
              "A concurrent request with the same idempotency key is still in progress.");

      return new CommandOutcome<TResult>(winner, WasReplayed: true);
    }
  }
}
