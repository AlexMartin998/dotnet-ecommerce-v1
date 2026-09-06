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
      // Atajo barato para el caso normal (ya procesado): evita abrir el efecto.
      // No es la garantía —esa es la clave primaria de abajo—, solo ahorra trabajo.
      if (await db.ProcessedMessages.AnyAsync(m => m.Id == messageId, token))
        return false;

      db.ProcessedMessages.Add(new ProcessedMessage { Id = messageId, Type = messageType });

      // ⚠️ El efecto va DENTRO, entre el Add y el SaveChanges. Ese orden es el arreglo
      // del P0: si el efecto lanza, la marca se deshace con él y el reintento puede
      // volver a intentarlo. Confirmar la marca antes dejaba los reintentos inertes.
      await effect(token);

      // El choque de clave primaria sale AQUÍ, y arrastra al efecto en el rollback.
      await db.SaveChangesAsync(token);

      return true;
    }, ct);
  }

  public bool IsConcurrentDuplicate(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);

    // Se recorre la cadena de InnerException en vez de mirar una forma concreta de
    // anidamiento: SaveChangesAsync envuelve el SqlException en DbUpdateException, pero
    // ExecuteUpdate* lo lanza desnudo. Es la regla §6 del proyecto.
    for (var current = exception; current is not null; current = current.InnerException)
      // 2627 = violación de PRIMARY KEY; 2601 = índice único.
      if (current is SqlException { Number: 2601 or 2627 }) return true;

    return false;
  }
}
