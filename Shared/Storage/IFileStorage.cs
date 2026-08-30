namespace ApiEcommerce.Shared.Storage;


/// <summary>
/// Almacenamiento de archivos. La implementación de hoy escribe en <c>wwwroot/</c>;
/// mañana puede ser S3 o Azure Blob sin tocar el servicio de productos.
/// </summary>
public interface IFileStorage
{
  /// <summary>
  /// Valida y guarda una imagen, devolviendo la <b>ruta relativa pública</b>
  /// (<c>/ProductsImages/xxx.jpg</c>).
  /// </summary>
  /// <remarks>
  /// Devuelve ruta relativa y <b>no una URL absoluta</b> a propósito. El código de
  /// referencia construía <c>{Request.Scheme}://{Request.Host}/...</c> y lo
  /// persistía: <c>Host</c> es una cabecera que controla el cliente (host header
  /// injection almacenada) y, además, la URL guardada queda rota en cuanto cambia el
  /// dominio o entra un proxy delante.
  /// </remarks>
  /// <exception cref="Exceptions.BadOperationAppException">
  /// El archivo está vacío, excede el tamaño máximo, o no es una imagen de un formato aceptado.
  /// </exception>
  Task<string> SaveProductImageAsync(FileUpload upload, CancellationToken ct = default);

  /// <summary>
  /// Borra el archivo correspondiente a una ruta relativa devuelta por
  /// <see cref="SaveProductImageAsync"/>. Es idempotente: si no existe, no hace nada.
  /// Ignora rutas que no apunten a la carpeta gestionada (p. ej. una URL externa).
  /// </summary>
  Task DeleteAsync(string? relativePath, CancellationToken ct = default);
}
