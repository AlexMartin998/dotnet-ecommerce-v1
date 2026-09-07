using System.Security.Cryptography;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Implementación de <see cref="ICommandLog"/> sobre <see cref="AppDbContext"/>.
/// </summary>
/// <remarks>
/// Escribe en la misma unidad de trabajo que el efecto, igual que <c>EventOutbox</c>, y es
/// <c>Scoped</c> por depender del <c>DbContext</c>. No hay reserva, TTL ni estado «en
/// curso»: entre réplicas arbitra el índice de la clave primaria.
/// </remarks>
public sealed class CommandLog(AppDbContext db) : ICommandLog
{
  /// <summary>
  /// Opciones de serialización de la huella. Deben ser estables entre procesos: dos
  /// réplicas tienen que calcular la misma huella para el mismo comando.
  /// </summary>
  private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);

  /// <inheritdoc />
  public async Task<TResult?> FindResultAsync<TResult>(
      CommandIntent intent, object command, CancellationToken ct = default) where TResult : class
  {
    if (!intent.IsDeclared) return null;

    var key = StorageKeyOf(intent);

    var existing = await db.ExecutedCommands
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == key, ct);

    if (existing is null) return null;

    // Misma intención con otro comando: 422. Devolver el resultado del primero sería
    // decirle al cliente que compró 5 lo que salió de comprar 1.
    if (existing.RequestHash != HashOf(command))
      throw new IdempotencyConflictAppException(
          "The idempotency key was already used for a different request. Use a new key.");

    return existing.Result is null ? null : JsonSerializer.Deserialize<TResult>(existing.Result, HashOptions);
  }

  /// <inheritdoc />
  public void Record<TResult>(CommandIntent intent, object command, TResult result) where TResult : class
  {
    if (!intent.IsDeclared) return;

    // Add y no SaveChanges: si el efecto falla después, la marca se deshace con él y el
    // reintento puede volver a intentarlo de verdad.
    db.ExecutedCommands.Add(new ExecutedCommand
    {
      Id = StorageKeyOf(intent),
      RequestHash = HashOf(command),
      Result = JsonSerializer.Serialize(result, HashOptions)
    });
  }

  /// <inheritdoc />
  public bool IsDuplicateIntent(Exception exception)
  {
    // Se recorre la cadena entera: SaveChangesAsync envuelve el SqlException en
    // DbUpdateException, pero ExecuteUpdate* lo lanza desnudo.
    for (var current = exception; current is not null; current = current.InnerException)
      // 2627 = PRIMARY KEY / UNIQUE; 2601 = índice único. Filtrar por número evita dar
      // por duplicado un timeout o un deadlock (1205).
      if (current is SqlException { Number: 2601 or 2627 }) return true;

    return false;
  }

  /// <summary>Huella estable del comando.</summary>
  private static string HashOf(object command)
      => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(command, HashOptions)));

  /// <summary>
  /// Clave de almacenamiento, comprobando que cabe en la columna.
  /// </summary>
  /// <remarks>
  /// Fallar aquí, con el nombre de la operación delante, se diagnostica solo; SQL Server
  /// daría un error críptico de longitud. Por HTTP no pasa con el tope por defecto de
  /// <c>Idempotency:MaxKeyLength</c>, pero sí un llamador nuevo.
  /// </remarks>
  private static string StorageKeyOf(CommandIntent intent)
  {
    var key = intent.StorageKey;

    return key.Length <= ExecutedCommandLimits.MaxKeyLength
        ? key
        : throw new ArgumentException(
            $"The command intent key is too long ({key.Length} > {ExecutedCommandLimits.MaxKeyLength}).",
            nameof(intent));
  }
}


/// <summary>Límites de <see cref="ExecutedCommand"/>, compartidos con su configuración de EF.</summary>
public static class ExecutedCommandLimits
{
  /// <summary>
  /// Longitud máxima de la clave. <c>nvarchar(400)</c> = 800 bytes, por debajo de los 900
  /// que admite una clave de índice en SQL Server.
  /// </summary>
  public const int MaxKeyLength = 400;
}
