using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Documents;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Documents;


/// <inheritdoc cref="IOrphanReceiptCollector"/>
/// <remarks>
/// Vive en <c>Ordering</c> y no en <c>Shared/Documents</c> porque solo quien tiene la tabla
/// sabe qué claves están referenciadas, y <c>Shared/</c> no puede nombrar tipos de
/// <c>Features/</c>.
/// </remarks>
public sealed class OrphanReceiptCollector(
    IDocumentStore documents,
    IOrderRepository orders,
    IOptions<DocumentStorageOptions> options,
    ILogger<OrphanReceiptCollector> logger) : IOrphanReceiptCollector
{
  private readonly DocumentStorageOptions _options = options.Value;

  /// <summary>Claves que se comprueban contra la base de una vez.</summary>
  /// <remarks>
  /// Ni una consulta por fichero ni el almacén entero en un <c>IN</c> que SQL Server no traga.
  /// </remarks>
  private const int BatchSize = 200;

  public async Task<int> CollectAsync(CancellationToken ct = default)
  {
    // Periodo de gracia: el fichero se escribe dentro de la transacción, así que existe antes que la fila que lo apunta.
    var cutoff = DateTime.Now.AddHours(-_options.OrphanGraceHours);

    var batch = new List<DocumentEntry>(BatchSize);
    var deleted = 0;
    var scanned = 0;

    await foreach (var entry in documents.ListAsync(cutoff, ct))
    {
      scanned++;
      batch.Add(entry);

      if (batch.Count < BatchSize) continue;

      deleted += await DeleteOrphansAsync(batch, ct);
      batch.Clear();
    }

    if (batch.Count > 0) deleted += await DeleteOrphansAsync(batch, ct);

    if (deleted > 0)
      logger.LogWarning(
          "Deleted {Deleted} orphan document(s) out of {Scanned} older than {Hours}h",
          deleted, scanned, _options.OrphanGraceHours);

    return deleted;
  }

  /// <summary>Borra las claves del lote que ninguna orden referencia.</summary>
  /// <remarks>
  /// Ante la duda no se borra: si la consulta de referencias falla se salta el lote entero,
  /// porque dejar basura una vuelta más cuesta menos que perder el documento de un cliente.
  /// </remarks>
  private async Task<int> DeleteOrphansAsync(IReadOnlyList<DocumentEntry> batch, CancellationToken ct)
  {
    IReadOnlySet<string> referenced;

    try
    {
      referenced = await orders.FindReferencedKeysAsync([.. batch.Select(e => e.Key)], ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogError(ex, "Could not resolve document references; skipping this batch");
      return 0;
    }

    var deleted = 0;

    foreach (var entry in batch)
    {
      if (referenced.Contains(entry.Key)) continue;

      ct.ThrowIfCancellationRequested();

      await documents.DeleteAsync(entry.Key, ct);

      logger.LogInformation(
          "Deleted orphan document {Key} ({Size} bytes, written {WrittenAt})",
          entry.Key, entry.SizeBytes, entry.WrittenAt);

      deleted++;
    }

    return deleted;
  }
}
