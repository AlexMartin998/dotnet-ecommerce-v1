using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Db;


/// <summary>Implementación sobre <see cref="AppDbContext"/> y su execution strategy.</summary>
public sealed class TransactionRunner(AppDbContext db) : ITransactionRunner
{
  public async Task<T> ExecuteAsync<T>(
      Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(operation);

    // Si ya hay una transacción abierta (p. ej. un [Transactional] por encima), se
    // participa en ella en vez de anidar otra: anidar transacciones en EF no hace lo
    // que la gente espera.
    if (db.Database.CurrentTransaction is not null)
      return await operation(ct);

    var strategy = db.Database.CreateExecutionStrategy();

    return await strategy.ExecuteAsync(async () =>
    {
      // Cada intento empieza limpio. Sin esto el reintento NO es equivalente: las
      // entidades añadidas en el intento anterior siguen rastreadas y, tras su
      // SaveChanges, quedaron como `Unchanged`, así que el segundo intento haría
      // commit de una transacción vacía — stock descontado sin evento, o al revés.
      db.ChangeTracker.Clear();

      await using var tx = await db.Database.BeginTransactionAsync(ct);

      try
      {
        var result = await operation(ct);

        // El commit va SIN token a propósito: cancelar un commit a medias es peor que
        // esperar a que termine.
        await tx.CommitAsync(CancellationToken.None);

        return result;
      }
      catch
      {
        // ⚠️ Y cada intento tiene que TERMINAR limpio, no solo empezar limpio. La
        // transacción se deshace sola al disponerse el `tx`, pero el change tracker NO:
        // las entidades del intento fallido siguen en `Added` sobre el DbContext del
        // scope. Con el código de hoy no explota —nadie más llama a SaveChanges en ese
        // request—, pero es una mina: cualquier SaveChanges posterior (un filtro, una
        // auditoría futura) insertaría esas filas **fuera de toda transacción**. Y afecta
        // a todo el que use el runner, no a un servicio concreto, así que se limpia aquí.
        db.ChangeTracker.Clear();
        throw;
      }
    });
  }
}
