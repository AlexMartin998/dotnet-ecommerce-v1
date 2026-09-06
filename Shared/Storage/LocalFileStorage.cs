using ApiEcommerce.Exceptions;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Storage;


/// <summary>
/// Guarda imágenes en <c>wwwroot/{ProductImagesFolder}</c>, validando antes de escribir.
/// </summary>
/// <remarks>
/// Limitación conocida y aceptada: el disco local no escala horizontalmente ni sobrevive
/// al redespliegue de un contenedor. <see cref="IFileStorage"/> existe para que pasar a
/// blob storage sea el cambio de una clase.
/// </remarks>
public sealed class LocalFileStorage(
    IWebHostEnvironment environment,
    IOptions<FileStorageOptions> options,
    ILogger<LocalFileStorage> logger) : IFileStorage
{
  private readonly FileStorageOptions _options = options.Value;

  /// <summary>
  /// Allowlist de extensiones, no denylist: estos archivos se publican desde el mismo
  /// origen que la API, así que uno malicioso (<c>.svg</c>, <c>.html</c>) sería XSS
  /// almacenado con sus cookies.
  /// </summary>
  private static readonly Dictionary<string, string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    [".jpg"] = "image/jpeg",
    [".jpeg"] = "image/jpeg",
    [".png"] = "image/png",
    [".gif"] = "image/gif",
    [".webp"] = "image/webp"
  };

  /// <inheritdoc />
  public async Task<string> SaveProductImageAsync(FileUpload upload, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(upload);

    if (upload.Length <= 0)
      throw new BadOperationAppException("The uploaded file is empty.");

    if (upload.Length > _options.MaxBytes)
      throw new BadOperationAppException(
          $"The file exceeds the maximum size of {_options.MaxBytes / 1024 / 1024} MB.");

    var extension = Path.GetExtension(upload.FileName);

    if (string.IsNullOrEmpty(extension) || !AllowedExtensions.TryGetValue(extension, out var expectedType))
      throw new BadOperationAppException(
          $"Unsupported file type. Allowed: {string.Join(", ", AllowedExtensions.Keys)}.");

    // Tercera comprobación: extensión y Content-Type los pone el cliente y se pueden
    // mentir los dos; la firma del archivo, no.
    if (!await LooksLikeImageAsync(upload.Content, expectedType, ct))
      throw new BadOperationAppException("The file content does not match a supported image format.");

    var folder = Path.Combine(WebRoot(), _options.ProductImagesFolder);
    Directory.CreateDirectory(folder);

    // El nombre lo genera el servidor: usar el del cliente, aunque sea solo la extensión,
    // es la puerta de entrada al path traversal.
    var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
    var fullPath = Path.Combine(folder, fileName);

    // Cinturón y tirantes: aunque el nombre es un GUID, se verifica la ruta resultante.
    EnsureInsideFolder(fullPath, folder);

    await using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                           bufferSize: 81920, useAsync: true))
    {
      upload.Content.Position = 0;
      await upload.Content.CopyToAsync(file, ct);   // async: una subida no bloquea un hilo del pool
    }

    logger.LogInformation("Stored product image {FileName} ({Bytes} bytes)", fileName, upload.Length);

    return $"/{_options.ProductImagesFolder}/{fileName}";
  }

  /// <inheritdoc />
  public Task DeleteAsync(string? relativePath, CancellationToken ct = default)
  {
    // Una URL externa (http://...) o un valor nulo no son archivos nuestros: no-op.
    if (string.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith($"/{_options.ProductImagesFolder}/", StringComparison.OrdinalIgnoreCase))
      return Task.CompletedTask;

    var folder = Path.Combine(WebRoot(), _options.ProductImagesFolder);
    var fullPath = Path.Combine(WebRoot(), relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    try
    {
      EnsureInsideFolder(fullPath, folder);

      if (File.Exists(fullPath))
      {
        File.Delete(fullPath);
        logger.LogInformation("Deleted product image {Path}", relativePath);
      }
    }
    catch (Exception ex)
    {
      // Un archivo huérfano es basura en disco; un 500 en el DELETE es un bug visible.
      logger.LogWarning(ex, "Could not delete product image {Path}", relativePath);
    }

    return Task.CompletedTask;
  }

  // ---- helpers privados ---------------------------------------------------

  /// <summary>
  /// <c>WebRootPath</c> y no <c>Directory.GetCurrentDirectory()</c>: el directorio de
  /// trabajo del proceso no es el del proyecto al publicar ni en un contenedor.
  /// </summary>
  private string WebRoot()
      => environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");

  /// <summary>Rechaza una ruta que se salga de la carpeta gestionada.</summary>
  private static void EnsureInsideFolder(string fullPath, string folder)
  {
    var resolved = Path.GetFullPath(fullPath);
    var root = Path.GetFullPath(folder);

    if (!resolved.StartsWith(root, StringComparison.Ordinal))
      throw new BadOperationAppException("Invalid file path.");
  }

  /// <summary>Comprueba la firma (magic bytes) del archivo contra el formato esperado.</summary>
  private static async Task<bool> LooksLikeImageAsync(Stream content, string expectedType, CancellationToken ct)
  {
    if (!content.CanSeek) return false;

    content.Position = 0;

    var header = new byte[12];
    var read = await content.ReadAsync(header, ct);

    content.Position = 0;

    if (read < 12) return false;

    return expectedType switch
    {
      "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
      "image/png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
      "image/gif" => header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38,
      // WEBP: "RIFF" .... "WEBP"
      "image/webp" => header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46
                   && header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50,
      _ => false
    };
  }
}
