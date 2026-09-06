using ApiEcommerce.Features.Ordering.Documents;
using ApiEcommerce.Features.Ordering.Messaging;
using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Messaging;
using QuestPDF.Infrastructure;

namespace ApiEcommerce.Features.Ordering;


/// <summary>
/// Registro del contexto acotado <b>Ordering</b>: órdenes y su comprobante.
/// </summary>
/// <remarks>
/// <para>
/// Es un contexto acotado propio y no una entidad más de <c>Catalog</c>: tiene su lenguaje
/// —orden, línea, comprobante, envío— y sus invariantes. Que hoy solo venda productos del
/// catálogo no lo convierte en parte de él, y de hecho toda la dependencia hacia allí cabe
/// en una clase (<see cref="CatalogGateway"/>).
/// </para>
/// <para>
/// Lo que <b>no</b> se registra aquí es el almacén de documentos: es transversal
/// (<c>Shared/Documents</c>), y quien decide si escribe en disco o en S3 es el despliegue,
/// no este slice.
/// </para>
/// </remarks>
public static class OrderingExtensions
{
  public static IServiceCollection AddOrderingFeature(
      this IServiceCollection services, IConfiguration configuration)
  {
    // ---- repositorio -------------------------------------------------------
    // No hereda de BaseRepository<T>: una orden no se actualiza ni se borra —se coloca, y
    // a partir de ahí solo cambia de estado—, así que de las cinco operaciones del CRUD
    // genérico no vale ninguna tal cual.
    services.AddScoped<IOrderRepository, OrderRepository>();

    // ---- puerto contra el catálogo -----------------------------------------
    // El ÚNICO punto del slice que conoce Catalog. El día que el catálogo sea otro
    // servicio, se cambia esta línea y esa clase.
    services.AddScoped<ICatalogGateway, CatalogGateway>();

    // ---- servicios ---------------------------------------------------------
    services.AddScoped<IOrderService, OrderService>();

    // ---- comprobante -------------------------------------------------------
    AddReceiptRendering(services);

    // El EFECTO de reaccionar a una orden colocada, separado del transporte. Se registra
    // SIEMPRE, también sin broker: es lógica del slice y así se puede probar sin AMQP
    // delante — la lección de planning/18.
    services.AddScoped<IReceiptGenerator, ReceiptGenerator>();

    services.AddEventConsumer<OrderPlacedConsumer>(
        configuration, OrderPlacedConsumer.Subscription);

    return services;
  }


  /// <summary>
  /// El motor de PDF. <b>Cambiar de librería es cambiar esta línea</b>: nada más del
  /// proyecto nombra QuestPDF.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>La licencia se declara AQUÍ, al arrancar, o QuestPDF lanza al GENERAR.</b> Sin
  /// esto la API arrancaría sana y los comprobantes fallarían uno a uno dentro del
  /// consumidor: cinco reintentos y a la DLQ, cada uno. Es una propiedad estática del
  /// proceso, así que este es su sitio natural — el composition root, una sola vez.
  /// </para>
  /// <para>
  /// <b>Community</b> es gratuita, también para uso comercial, con ingresos brutos anuales
  /// <b>por debajo de 1.000.000 USD</b>. ⚠️ Es un umbral, no un «gratis para siempre»:
  /// superarlo exige licencia (con 90 días de transición) y eso es una <b>decisión del
  /// owner</b>, anotada en <c>planning/20</c> §20.7. Se comprobó antes de meterlo por lo
  /// que pasó con AutoMapper 15, que empezó a exigir licencia comercial con el proyecto ya
  /// montado.
  /// </para>
  /// <para>
  /// Singleton: el renderizador no guarda estado entre documentos y crear uno por
  /// comprobante no aporta nada.
  /// </para>
  /// </remarks>
  private static void AddReceiptRendering(IServiceCollection services)
  {
    QuestPDF.Settings.License = LicenseType.Community;

    services.AddSingleton<IReceiptRenderer, QuestPdfReceiptRenderer>();
  }
}
