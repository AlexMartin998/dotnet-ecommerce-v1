namespace ApiEcommerce.Shared.Documents;


/// <summary>
/// Almacén de documentos <b>privados</b> (comprobantes, facturas): se guardan, se leen y
/// se borran por una <b>clave opaca</b>, nunca por una ruta.
/// </summary>
/// <remarks>
/// <para>
/// Es el puerto que permite que hoy sea el sistema de ficheros y mañana S3, R2, MinIO o
/// Cloudinary <b>sin tocar ni el dominio, ni el consumidor, ni el controller</b>: cambia
/// la implementación que se registra en el composition root y nada más.
/// </para>
/// <para>
/// ⚠️ <b>No es <see cref="Storage.IFileStorage"/>, y no debe fusionarse con él.</b> Aquel
/// guarda imágenes de producto en <c>wwwroot/</c> para que <c>UseStaticFiles</c> las sirva
/// a cualquiera: son públicas y esa es su gracia. Estas no. Un comprobante lleva el nombre
/// del cliente, su dirección y lo que pagó; ponerlo bajo <c>wwwroot/</c> lo deja al alcance
/// de quien adivine la ruta, sin pasar por autenticación. Dos necesidades opuestas no
/// caben detrás de la misma abstracción por mucho que las dos "guarden ficheros".
/// </para>
/// <para>
/// <b>La clave es OPACA</b>. Quien llama la guarda y la devuelve, y no puede construirla,
/// interpretarla ni convertirla en una ruta. Eso es lo que hace que persistirla en base de
/// datos siga siendo válido después de migrar de infraestructura: si ahí hubiera
/// <c>/app/documents/2026/09/x.pdf</c>, el día del cambio habría que reescribir todas las
/// filas.
/// </para>
/// </remarks>
public interface IDocumentStore
{
  /// <summary>Guarda un documento y devuelve la clave con la que recuperarlo.</summary>
  /// <remarks>
  /// La clave la genera el almacén, e incluye una parte <b>aleatoria</b>: no se puede
  /// deducir de la orden ni del usuario. Aunque el acceso ya esté protegido, una clave
  /// adivinable convierte cualquier despiste futuro en una fuga.
  /// </remarks>
  /// <param name="content">Contenido y metadatos del documento.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<DocumentReference> SaveAsync(DocumentContent content, CancellationToken ct = default);

  /// <summary>Abre un documento por su clave, o <c>null</c> si no existe.</summary>
  /// <remarks>
  /// Devuelve <c>null</c> y <b>no lanza</b> cuando no está: que un documento haya
  /// desaparecido del almacén es una condición que quien llama tiene que poder tratar
  /// —el comprobante puede estar todavía generándose— y no un fallo del sistema.
  /// </remarks>
  /// <param name="key">La clave devuelta por <see cref="SaveAsync"/>.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<DocumentContent?> OpenAsync(string key, CancellationToken ct = default);

  /// <summary>Borra un documento. Idempotente: si no existe, no hace nada.</summary>
  Task DeleteAsync(string key, CancellationToken ct = default);

  /// <summary>
  /// Enumera los documentos escritos <b>antes</b> de un instante dado.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Existe para que alguien pueda recoger la basura: el documento se escribe <b>dentro</b>
  /// de la transacción que lo referencia, así que un commit fallido deja un fichero que
  /// ninguna fila apunta. Sin poder enumerar, esos huérfanos no se pueden encontrar nunca.
  /// </para>
  /// <para>
  /// ⚠️ El filtro por fecha es parte del contrato y <b>no una comodidad</b>: entre que el
  /// fichero existe y existe la fila que lo apunta hay una ventana, así que quien recoja
  /// basura tiene que poder decir «solo lo viejo». Ver
  /// <c>DocumentStorageOptions.OrphanGraceHours</c>.
  /// </para>
  /// <para>
  /// Devuelve un flujo y no una lista: todo almacén sabe hacer esto (S3 <c>ListObjects</c>,
  /// R2, MinIO, Cloudinary), pero todos pagan por página, y materializar un bucket entero
  /// en memoria para borrar tres ficheros no es una opción.
  /// </para>
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
/// Lleva <see cref="Stream"/> y no <c>byte[]</c> a propósito: un comprobante son unas
/// decenas de KB, pero la misma interfaz servirá mañana para adjuntos grandes, y meter el
/// fichero entero en memoria por cada descarga es la clase de decisión que solo duele
/// cuando ya hay tráfico. Quien lo recibe es responsable de liberarlo.
/// </remarks>
/// <param name="Stream">El contenido. <b>Hay que liberarlo</b>.</param>
/// <param name="ContentType">Tipo MIME (<c>application/pdf</c>).</param>
/// <param name="FileName">
/// Nombre sugerido para la descarga (<c>ORD-2026-000012.pdf</c>). Es de presentación: el
/// almacén no lo usa para localizar nada.
/// <para>
/// ⚠️ Al <b>abrir</b>, el almacén solo puede devolver lo que sabe —el nombre derivado de
/// la clave—, y eso no es un nombre presentable: quien descarga acaba con
/// <c>cdfdcf87….pdf</c> en su carpeta. Cómo se llama el documento de cara al usuario es
/// conocimiento del dominio, así que quien lo sirve lo reemplaza (<c>content with
/// { FileName = … }</c>). Guardarlo en el almacén para poder devolverlo sería meter
/// presentación en la infraestructura, y obligaría a cada proveedor futuro a tener dónde
/// ponerlo.
/// </para>
/// </param>
public sealed record DocumentContent(Stream Stream, string ContentType, string FileName) : IAsyncDisposable
{
  public ValueTask DisposeAsync() => Stream.DisposeAsync();
}
