using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Documents;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Documents;


/// <inheritdoc cref="IOrphanReceiptCollector"/>
/// <remarks>
/// ⚠️ <b>Vive en <c>Ordering</c> y no en <c>Shared/Documents</c></b>, aunque hable de un
/// almacén transversal: la pregunta «¿quién referencia esta clave?» solo la sabe responder
/// quien tiene la tabla, y <c>Shared/</c> no puede nombrar tipos de <c>Features/</c>
/// (<c>rules.md</c> §4). Hoy <c>Ordering</c> es el único que escribe documentos; el día que
/// haya un segundo, esto se invierte con un puerto <c>IDocumentReferences</c> y sube a
/// <c>Shared</c>.
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
  /// Ni una consulta por fichero —una tormenta contra la base— ni el almacén entero en una
  /// sola —un <c>IN</c> de cien mil elementos que SQL Server no traga—. Un lote acotado.
  /// </remarks>
  private const int BatchSize = 200;

  public async Task<int> CollectAsync(CancellationToken ct = default)
  {
    // ⚠️ 🔴 EL periodo de gracia, y la única línea de todo esto que no se puede equivocar.
    // El fichero existe ANTES que la fila que lo apunta —se escribe dentro de la
    // transacción—, así que sin este corte se borrarían comprobantes buenos a mitad de
    // vuelo. Y un comprobante borrado no vuelve.
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
  /// ⚠️ <b>Ante la duda, no se borra.</b> Si la consulta de referencias falla se salta el
  /// lote entero: borrar de más pierde el documento de un cliente, borrar de menos deja
  /// basura una vuelta más. La asimetría del coste decide sola.
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
