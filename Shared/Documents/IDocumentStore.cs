namespace ApiEcommerce.Shared.Documents;


/// <summary>
/// Almacén de documentos privados (comprobantes, facturas): se guardan, se leen y se
/// borran por una clave opaca, nunca por una ruta.
/// </summary>
/// <remarks>
/// No es <see cref="Storage.IFileStorage"/> ni debe fusionarse con él: aquel sirve
/// imágenes públicas desde <c>wwwroot/</c> y un comprobante lleva datos del cliente. La
/// clave es opaca para que persistirla siga valiendo tras migrar de infraestructura.
/// </remarks>
public interface IDocumentStore
{
  /// <summary>Guarda un documento y devuelve su <see cref="DocumentReference"/>: clave y tamaño.</summary>
  /// <remarks>
  /// La clave la genera el almacén e incluye una parte aleatoria: una clave adivinable
  /// convierte cualquier despiste futuro en una fuga.
  /// </remarks>
  /// <param name="content">Contenido y metadatos del documento.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<DocumentReference> SaveAsync(DocumentContent content, CancellationToken ct = default);

  /// <summary>Abre un documento por su clave, o <c>null</c> si no existe.</summary>
  /// <remarks>
  /// Devuelve <c>null</c> y no lanza: que el documento no esté todavía (se está generando)
  /// es una condición que quien llama debe poder tratar, no un fallo del sistema.
  /// </remarks>
  /// <param name="key">La clave devuelta por <see cref="SaveAsync"/>.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<DocumentContent?> OpenAsync(string key, CancellationToken ct = default);

  /// <summary>Borra un documento. Idempotente: si no existe, no hace nada.</summary>
  Task DeleteAsync(string key, CancellationToken ct = default);

  /// <summary>
  /// Enumera los documentos escritos antes de un instante dado, para poder recoger la basura.
  /// </summary>
  /// <remarks>
  /// El filtro por fecha es parte del contrato: entre el fichero y la fila que lo apunta
  /// hay una ventana (ver <c>DocumentStorageOptions.OrphanGraceHours</c>). Devuelve un
  /// flujo porque todo almacén pagina y materializar un bucket entero no es una opción.
  /// </remarks>
  /// <param name="writtenBefore">Solo documentos escritos antes de este instante.</param>
  /// <param name="ct">Token de cancelación.</param>
  IAsyncEnumerable<DocumentEntry> ListAsync(DateTime writtenBefore, CancellationToken ct = default);
}


/// <summary>Un documento enumerado en el almacén.</summary>
/// <param name="Key">Su clave opaca, la misma que devolvió <c>SaveAsync</c>.</param>
/// <param name="WrittenAt">Cuándo se escribió, para poder aplicar el periodo de gracia.</param>
/// <param name="SizeBytes">Tamaño.</param>
public readonly record struct DocumentEntry(string Key, DateTime WrittenAt, long SizeBytes);


/// <summary>Un documento guardado.</summary>
/// <param name="Key">Clave opaca. Es lo único que se persiste en base de datos.</param>
/// <param name="SizeBytes">Tamaño, para poder emitir <c>Content-Length</c> sin abrirlo.</param>
public readonly record struct DocumentReference(string Key, long SizeBytes);


/// <summary>Contenido de un documento, con lo que hace falta para servirlo por HTTP.</summary>
/// <remarks>
/// Lleva <see cref="Stream"/> y no <c>byte[]</c> pensando en adjuntos grandes: meter el
/// fichero entero en memoria por cada descarga solo duele cuando ya hay tráfico.
/// </remarks>
/// <param name="Stream">El contenido. Hay que liberarlo.</param>
/// <param name="ContentType">Tipo MIME (<c>application/pdf</c>).</param>
/// <param name="FileName">
/// Nombre sugerido para la descarga (<c>ORD-2026-000012.pdf</c>). Es de presentación: al
/// abrir, el almacén solo puede derivarlo de la clave, así que quien lo sirve lo reemplaza
/// (<c>content with { FileName = … }</c>).
/// </param>
public sealed record DocumentContent(Stream Stream, string ContentType, string FileName) : IAsyncDisposable
{
  public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
