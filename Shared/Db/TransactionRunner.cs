using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Db;


/// <summary>Implementación sobre <see cref="AppDbContext"/> y su execution strategy.</summary>
public sealed class TransactionRunner(AppDbContext db) : ITransactionRunner
{
  /// <inheritdoc />
  public async Task<T> ExecuteAsync<T>(
      Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(operation);

    // Con una transacción ya abierta se participa en ella: anidar transacciones en EF no
    // hace lo que la gente espera.
    if (db.Database.CurrentTransaction is not null)
      return await operation(ct);

    var strategy = db.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async () =>
    {
      // Cada intento empieza limpio: las entidades del intento anterior quedaron como
      // `Unchanged` y el segundo haría commit de una transacción vacía.
      db.ChangeTracker.Clear();

      await using var tx = await db.Database.BeginTransactionAsync(ct);

      try
      {
        var result = await operation(ct);

        // El commit va sin token: cancelarlo a medias es peor que esperar a que termine.
        await tx.CommitAsync(CancellationToken.None);

        return result;
      }
      catch
      {
        // Y cada intento tiene que terminar limpio: la transacción se deshace sola al
        // disponerse el `tx`, pero el change tracker no, y un SaveChanges posterior
        // insertaría esas filas fuera de toda transacción.
        db.ChangeTracker.Clear();
        throw;
      }
    });
  }
}
