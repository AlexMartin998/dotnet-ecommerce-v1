using ApiEcommerce.Features.Ordering.Documents;
using ApiEcommerce.Features.Ordering.Messaging;
using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Messaging;
using QuestPDF.Infrastructure;

namespace ApiEcommerce.Features.Ordering;


/// <summary>Registro del contexto acotado Ordering: órdenes y su comprobante.</summary>
/// <remarks>
/// Es un contexto acotado propio, con su lenguaje e invariantes, y toda su dependencia del
/// catálogo cabe en <see cref="CatalogGateway"/>. El almacén de documentos no se registra
/// aquí: es transversal y lo decide el despliegue.
/// </remarks>
public static class OrderingExtensions
{
  public static IServiceCollection AddOrderingFeature(
      this IServiceCollection services, IConfiguration configuration)
  {
    // ---- repositorio -------------------------------------------------------
    // No hereda de BaseRepository<T>: una orden se coloca y luego solo cambia de estado.
    services.AddScoped<IOrderRepository, OrderRepository>();

    // ---- puerto contra el catálogo -----------------------------------------
    // El único punto del slice que conoce Catalog.
    services.AddScoped<ICatalogGateway, CatalogGateway>();

    // ---- servicios ---------------------------------------------------------
    services.AddScoped<IOrderService, OrderService>();

    // ---- comprobante -------------------------------------------------------
    AddReceiptRendering(services);

    // Se registra siempre, también sin broker: es lógica del slice, no transporte.
    services.AddScoped<IReceiptGenerator, ReceiptGenerator>();

    services.AddEventConsumer<OrderPlacedConsumer>(
        configuration, OrderPlacedConsumer.Subscription);

    // ---- recolección de basura ---------------------------------------------
    // También sin broker: el huérfano lo produce un commit fallido, no el transporte.
    // Se apaga con `Documents:CleanupIntervalHours = 0`.
    services.AddScoped<IOrphanReceiptCollector, OrphanReceiptCollector>();
    services.AddHostedService<ReceiptCleaner>();

    return services;
  }


  /// <summary>
  /// El motor de PDF: la licencia y el renderizador. Cambiar de librería toca aquí y
  /// <c>QuestPdfReceiptRenderer</c>, los dos únicos sitios que nombran QuestPDF.
  /// </summary>
  /// <remarks>
  /// La licencia se declara aquí al arrancar o QuestPDF lanza al generar, ya con la API en
  /// pie. Community es gratuita por debajo de 1.000.000 USD de ingresos brutos anuales.
  /// Singleton porque el renderizador no guarda estado entre documentos.
  /// </remarks>
  private static void AddReceiptRendering(IServiceCollection services)
  {
    QuestPDF.Settings.License = LicenseType.Community;

    services.AddSingleton<IReceiptRenderer, QuestPdfReceiptRenderer>();
  }
}
