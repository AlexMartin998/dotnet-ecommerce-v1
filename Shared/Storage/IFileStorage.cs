namespace ApiEcommerce.Shared.Storage;


/// <summary>
/// Almacenamiento de archivos PÚBLICOS. La implementación de hoy escribe en
/// <c>wwwroot/</c>; mañana puede ser S3 o Azure Blob sin tocar a quien la use.
/// </summary>
/// <remarks>
/// No nombra ninguna entidad de dominio: vive en <c>Shared/</c> y hoy solo lo usa el
/// catálogo, pero cualquier slice podría guardar una imagen pública. El gemelo privado es
/// <see cref="Documents.IDocumentStore"/>, y no se fusionan: la pregunta que los separa no
/// es «¿qué guarda?» sino «¿quién puede leerlo?».
/// </remarks>
public interface IFileStorage
{
  /// <summary>
  /// Valida y guarda una imagen, devolviendo su ruta relativa pública
  /// (<c>/ProductsImages/xxx.jpg</c>, según <c>Storage:ProductImagesFolder</c>).
  /// </summary>
  /// <remarks>
  /// Relativa y no absoluta a propósito: construirla con <c>Request.Host</c> y persistirla
  /// es host header injection almacenada, y la URL queda rota al cambiar de dominio o al
  /// poner un proxy delante.
  /// </remarks>
  /// <exception cref="Exceptions.BadOperationAppException">
  /// El archivo está vacío, excede el tamaño máximo, o no es una imagen de un formato aceptado.
  /// </exception>
  Task<string> SaveImageAsync(FileUpload upload, CancellationToken ct = default);

  /// <summary>
  /// Borra el archivo de una ruta relativa devuelta por
  /// <see cref="SaveImageAsync"/>. Idempotente, e ignora las rutas que no apunten a
  /// la carpeta gestionada.
  /// </summary>
  Task DeleteAsync(string? relativePath, CancellationToken ct = default);
}
