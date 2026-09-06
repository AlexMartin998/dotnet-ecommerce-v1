namespace ApiEcommerce.Shared.Storage;


/// <summary>Registro en DI del almacenamiento de archivos.</summary>
public static class StorageExtensions
{
  /// <summary>
  /// Almacenamiento de archivos. Hoy es disco local; este registro es el único punto que
  /// hay que cambiar el día que sea S3 o Azure Blob.
  /// </summary>
  public static IServiceCollection AddFileStorage(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<FileStorageOptions>()
        .Bind(configuration.GetSection(FileStorageOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();   // configuración inválida = no arranca, no falla en la primera petición

    services.AddSingleton<IFileStorage, LocalFileStorage>();

    return services;
  }
}
