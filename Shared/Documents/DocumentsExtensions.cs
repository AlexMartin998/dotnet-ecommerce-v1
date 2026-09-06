namespace ApiEcommerce.Shared.Documents;


/// <summary>Registro en DI del almacén de documentos privados.</summary>
public static class DocumentsExtensions
{
  /// <summary>
  /// Registra el almacén de documentos privados. Es el único sitio que hay que tocar el
  /// día que los comprobantes vivan en S3, R2, MinIO o Cloudinary.
  /// </summary>
  /// <remarks>
  /// La elección se hace al arrancar, como en <c>AddDistributedCaching</c> y
  /// <c>AddMessaging</c>: lo que se decide es qué implementación se registra. Un proveedor
  /// desconocido tumba el arranque en vez de degradar a disco en silencio.
  /// </remarks>
  public static IServiceCollection AddDocumentStorage(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<DocumentStorageOptions>()
        .Bind(configuration.GetSection(DocumentStorageOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    var options = configuration.GetSection(DocumentStorageOptions.SectionName)
                      .Get<DocumentStorageOptions>()
                  ?? new DocumentStorageOptions();

    switch (options.Provider.Trim().ToLowerInvariant())
    {
      case DocumentStorageOptions.FileSystemProvider:
        // Singleton: no guarda estado por petición y sus dependencias ya son singletons.
        services.AddSingleton<IDocumentStore, LocalDocumentStore>();
        break;

      default:
        throw new InvalidOperationException(
            $"Unknown document storage provider '{options.Provider}'. " +
            $"Known providers: '{DocumentStorageOptions.FileSystemProvider}'.");
    }

    return services;
  }
}
