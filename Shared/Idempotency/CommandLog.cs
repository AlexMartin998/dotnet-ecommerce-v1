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
/// <para>
/// Escribe en la misma unidad de trabajo que el efecto de negocio, igual que
/// <c>EventOutbox</c>: eso es lo que hace que la marca y el efecto se confirmen juntos
/// o no se confirmen. <c>Scoped</c>, porque depende del <c>DbContext</c>.
/// </para>
/// <para>
/// ⚠️ No hay ni reserva, ni TTL, ni token de propiedad, ni estado «en curso». Toda esa
/// maquinaria existía en la versión sobre Redis para emular, mal, lo que la base de
/// datos ya hace: si dos réplicas insertan la misma clave primaria a la vez, la segunda
/// se <b>bloquea</b> hasta que la primera confirme y entonces choca. El árbitro es el
/// índice, no el código.
/// </para>
/// </remarks>
public sealed class CommandLog(AppDbContext db) : ICommandLog
{
  /// <summary>
  /// Opciones de serialización de la huella. Deben ser <b>estables entre procesos</b>:
  /// dos réplicas tienen que calcular la misma huella para el mismo comando.
  /// </summary>
  private static readonly JsonSerializerOptions HashOptions = new(JsonSerializerDefaults.Web);

  public async Task<TResult?> FindResultAsync<TResult>(
      CommandIntent intent, object command, CancellationToken ct = default) where TResult : class
  {
    if (!intent.IsDeclared) return null;

    var key = StorageKeyOf(intent);

    var existing = await db.ExecutedCommands
        .AsNoTracking()
        .FirstOrDefaultAsync(c => c.Id == key, ct);

    if (existing is null) return null;

    // Misma intención, otro comando: 422. Devolver el resultado del primero sería el
    // fallo silencioso que este mecanismo existe para evitar — el cliente pidió comprar
    // 5 y recibiría el resultado de haber comprado 1, sin que nada lo indicara.
    if (existing.RequestHash != HashOf(command))
      throw new IdempotencyConflictAppException(
          "The idempotency key was already used for a different request. Use a new key.");

    return existing.Result is null ? null : JsonSerializer.Deserialize<TResult>(existing.Result, HashOptions);
  }

  public void Record<TResult>(CommandIntent intent, object command, TResult result) where TResult : class
  {
    if (!intent.IsDeclared) return;

    // Add y NO SaveChanges: quien confirma es la transacción de negocio. Si el efecto
    // falla después de esta línea, la marca se deshace con él y el reintento puede
    // volver a intentarlo de verdad. Es la lección del P0 del consumidor: confirmar la
    // marca antes que el efecto deja reintentos inertes.
    db.ExecutedCommands.Add(new ExecutedCommand
    {
      Id = StorageKeyOf(intent),
      RequestHash = HashOf(command),
      Result = JsonSerializer.Serialize(result, HashOptions)
    });
  }

  public bool IsDuplicateIntent(Exception exception)
  {
    // ⚠️ Se recorre la cadena de InnerException en vez de mirar una forma concreta de
    // anidamiento: SaveChangesAsync envuelve el SqlException en DbUpdateException, pero
    // ExecuteUpdate* lo lanza desnudo. Es la regla §6 del proyecto.
    for (var current = exception; current is not null; current = current.InnerException)
      // 2627 = violación de PRIMARY KEY / UNIQUE constraint; 2601 = índice único.
      // Filtrar por número es imprescindible: un catch por tipo se tragaría también
      // timeouts y deadlocks (1205) y los daría por "duplicado", que es justo el fallo
      // que ya se corrigió en ProductPurchasedConsumer.
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
  /// Si no cabe, SQL Server truncaría o lanzaría un error críptico de longitud. Que falle
  /// aquí, con el nombre de la operación delante, cuesta lo mismo y se diagnostica solo.
  /// Por HTTP no puede pasar —el filtro acota la clave del cliente antes—, pero un
  /// llamador nuevo sí podría.
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
