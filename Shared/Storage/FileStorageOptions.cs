using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Storage;


/// <summary>Límites y ubicación del almacenamiento de imágenes (sección <c>Storage</c>).</summary>
public sealed class FileStorageOptions
{
  public const string SectionName = "Storage";

  /// <summary>Carpeta bajo <c>wwwroot/</c> donde se guardan las imágenes de producto.</summary>
  public string ProductImagesFolder { get; init; } = "ProductsImages";

  /// <summary>Tamaño máximo por archivo.</summary>
  [Range(1, 20 * 1024 * 1024)]
  public long MaxBytes { get; init; } = 2 * 1024 * 1024; // 2 MB
}
