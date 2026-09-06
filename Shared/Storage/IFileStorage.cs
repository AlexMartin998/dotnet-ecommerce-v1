namespace ApiEcommerce.Shared.Storage;


/// <summary>
/// Almacenamiento de archivos públicos. La implementación de hoy escribe en
/// <c>wwwroot/</c>; mañana puede ser S3 o Azure Blob sin tocar el servicio de productos.
/// </summary>
public interface IFileStorage
{
  /// <summary>
  /// Valida y guarda una imagen, devolviendo la ruta relativa pública
  /// (<c>/ProductsImages/xxx.jpg</c>).
  /// </summary>
  /// <remarks>
  /// Relativa y no absoluta a propósito: construirla con <c>Request.Host</c> y persistirla
  /// es host header injection almacenada, y la URL queda rota al cambiar de dominio o al
  /// poner un proxy delante.
  /// </remarks>
  /// <exception cref="Exceptions.BadOperationAppException">
  /// El archivo está vacío, excede el tamaño máximo, o no es una imagen de un formato aceptado.
  /// </exception>
  Task<string> SaveProductImageAsync(FileUpload upload, CancellationToken ct = default);

  /// <summary>
  /// Borra el archivo de una ruta relativa devuelta por
  /// <see cref="SaveProductImageAsync"/>. Idempotente, e ignora las rutas que no apunten a
  /// la carpeta gestionada.
  /// </summary>
  Task DeleteAsync(string? relativePath, CancellationToken ct = default);
}
