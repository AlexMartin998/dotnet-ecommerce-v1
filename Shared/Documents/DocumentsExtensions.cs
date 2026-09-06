namespace ApiEcommerce.Shared.Documents;


public static class DocumentsExtensions
{
  /// <summary>
  /// Almacén de documentos privados. <b>Este método es el único sitio que hay que tocar</b>
  /// el día que los comprobantes vivan en S3, R2, MinIO o Cloudinary.
  /// </summary>
  /// <remarks>
  /// <para>
  /// La elección se hace <b>al arrancar</b> y no por petición, que es lo correcto cuando
  /// lo que se decide es <i>qué implementación se registra</i>: el grafo de DI se
  /// construye una vez. Es la misma forma que ya usan <c>AddDistributedCaching</c> y
  /// <c>AddMessaging</c>.
  /// </para>
  /// <para>
  /// ⚠️ Un proveedor desconocido <b>tumba el arranque</b> en vez de caer al sistema de
  /// ficheros. Un <c>"s3"</c> mal escrito que degradara a disco escribiría comprobantes en
  /// un contenedor efímero creyendo que están en el bucket — y eso no se descubre hasta el
  /// primer reinicio, cuando ya se han perdido.
  /// </para>
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
        // El día que sea S3, el cliente del SDK también es caro de crear y también va aquí.
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
