using ApiEcommerce.Data;
using ApiEcommerce.Shared.Db;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>Implementación de <see cref="IMessageInbox"/> sobre <c>ProcessedMessages</c>.</summary>
public sealed class MessageInbox(AppDbContext db, ITransactionRunner transactions) : IMessageInbox
{
  public async Task<bool> ProcessOnceAsync(
      Guid messageId, string messageType, Func<CancellationToken, Task> effect,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(effect);

    return await transactions.ExecuteAsync(async token =>
    {
      // Atajo barato para el caso normal; la garantía real es la clave primaria de abajo.
      if (await db.ProcessedMessages.AnyAsync(m => m.Id == messageId, token))
        return false;

      db.ProcessedMessages.Add(new ProcessedMessage { Id = messageId, Type = messageType });

      // El efecto va entre el Add y el SaveChanges: si lanza, la marca se deshace con él.
      await effect(token);

      // El choque de clave primaria sale AQUÍ, y arrastra al efecto en el rollback.
      await db.SaveChangesAsync(token);

      return true;
    }, ct);
  }

  public bool IsConcurrentDuplicate(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);

    // Se recorre toda la cadena: SaveChangesAsync anida el SqlException, ExecuteUpdate* no.
    for (var current = exception; current is not null; current = current.InnerException)
      // 2627 = violación de PRIMARY KEY; 2601 = índice único.
      if (current is SqlException { Number: 2601 or 2627 }) return true;

    return false;
  }
}
