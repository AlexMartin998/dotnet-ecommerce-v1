using System.Net.Http.Json;
using System.Text;
using ApiEcommerce.Features.Ordering.Documents;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Documents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ApiEcommerce.Tests.Integration;


/// <summary>El recolector de comprobantes huérfanos.</summary>
/// <remarks>
/// Este componente borra ficheros, así que los tests que importan son los que comprueban
/// que no borra. Usa el almacén y la base reales, porque su trabajo entero es la
/// interacción entre los dos.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class OrphanReceiptCollectorTests(ApiFactory factory)
{
  private static DocumentContent Pdf() =>
      new(new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4 huerfano")), "application/pdf", "x.pdf");

  private IDocumentStore Documents => factory.Services.GetRequiredService<IDocumentStore>();

  /// <summary>Un recolector con la gracia que pida el test.</summary>
  /// <remarks>
  /// La gracia es la variable del componente: distingue «viejo y sin dueño» de «recién
  /// escrito», así que se inyecta en vez de tocar la del host.
  /// </remarks>
  private OrphanReceiptCollector Collector(int graceHours, IOrderRepository? orders = null)
  {
    var scope = factory.Services.CreateScope();

    return new OrphanReceiptCollector(
        Documents,
        orders ?? scope.ServiceProvider.GetRequiredService<IOrderRepository>(),
        Options.Create(new DocumentStorageOptions { OrphanGraceHours = graceHours }),
        NullLogger<OrphanReceiptCollector>.Instance);
  }

  /// <summary>Envejece un fichero para que caiga del lado viejo del corte.</summary>
  private string Age(string key, TimeSpan by)
  {
    // La clave es lo único que se sabe del documento: la ruta sale de la raíz configurada.
    var root = factory.Services.GetRequiredService<IOptions<DocumentStorageOptions>>().Value.RootPath;
    var path = Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));

    File.SetLastWriteTime(path, DateTime.Now - by);

    return path;
  }

  // ---- lo que SÍ se borra --------------------------------------------------

  [Fact]
  public async Task CollectsADocumentThatNoOrderReferences()
  {
    var saved = await Documents.SaveAsync(Pdf());
    Age(saved.Key, TimeSpan.FromHours(48));

    var deleted = await Collector(graceHours: 24).CollectAsync();

    Assert.True(deleted >= 1);
    Assert.Null(await Documents.OpenAsync(saved.Key));
  }

  [Fact]
  public async Task ATruncatedFileNeedsNoSpecialCase()
  {
    // Un PDF truncado no tiene clave devuelta, así que nadie lo referencia: cae por la
    // misma regla que cualquier huérfano y no necesita caso aparte.
    var root = factory.Services.GetRequiredService<IOptions<DocumentStorageOptions>>().Value.RootPath;
    var folder = Path.Combine(root, "2020", "01");
    Directory.CreateDirectory(folder);

    var truncated = Path.Combine(folder, $"{Guid.NewGuid():N}.pdf");
    await File.WriteAllTextAsync(truncated, "%PDF-1.4 a med");
    File.SetLastWriteTime(truncated, DateTime.Now.AddHours(-48));

    await Collector(graceHours: 24).CollectAsync();

    Assert.False(File.Exists(truncated));
  }

  // ---- lo que NUNCA se borra (los tests que de verdad importan) -------------

  [Fact]
  public async Task NeverCollectsADocumentAnOrderPointsAt()
  {
    // Borrar de más es perder el comprobante de un cliente.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceOrderAsync(user, sku);

    var key = await ReceiptKeyOf(order);

    Assert.NotNull(key);
    Age(key!, TimeSpan.FromHours(48));   // viejo de sobra: solo lo salva estar referenciado

    await Collector(graceHours: 24).CollectAsync();

    await using var still = await Documents.OpenAsync(key!);
    Assert.NotNull(still);
  }

  [Fact]
  public async Task NeverCollectsAFileThatWasJustWritten()
  {
    // El PDF existe un rato antes que la fila que lo apunta: sin periodo de gracia, el
    // recolector borraría comprobantes buenos a mitad de vuelo.
    var saved = await Documents.SaveAsync(Pdf());   // recién escrito, sin dueño todavía

    var deleted = await Collector(graceHours: 24).CollectAsync();

    await using var still = await Documents.OpenAsync(saved.Key);

    Assert.NotNull(still);
    Assert.Equal(0, deleted);
  }

  [Fact]
  public async Task WhenTheDatabaseCannotAnswerNothingIsDeleted()
  {
    // Ante la duda no se borra: saltarse el lote deja basura una vuelta, borrarlo pierde
    // documentos para siempre.
    var saved = await Documents.SaveAsync(Pdf());
    Age(saved.Key, TimeSpan.FromHours(48));

    var broken = new Mock<IOrderRepository>();
    broken.Setup(r => r.FindReferencedKeysAsync(
              It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new InvalidOperationException("la base no contesta"));

    var deleted = await Collector(graceHours: 24, orders: broken.Object).CollectAsync();

    Assert.Equal(0, deleted);
    await using var still = await Documents.OpenAsync(saved.Key);
    Assert.NotNull(still);
  }

  // ---- helpers -------------------------------------------------------------

  private static async Task<int> PlaceOrderAsync(HttpClient client, string sku)
  {
    var response = await client.PostAsJsonAsync("/api/v1/order", new
    {
      items = new[] { new { sku, quantity = 1 } },
      customerName = "Recolector"
    });

    response.EnsureSuccessStatusCode();

    return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>())
        .GetProperty("id").GetInt32();
  }

  private async Task<string?> ReceiptKeyOf(int orderId)
  {
    using var scope = factory.Services.CreateScope();

    var generator = scope.ServiceProvider
        .GetRequiredService<ApiEcommerce.Features.Ordering.Messaging.IReceiptGenerator>();

    var orders = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
    var order = await orders.FindWithItemsAsync(orderId);

    await generator.HandleAsync(new ApiEcommerce.Features.Ordering.Events.OrderPlaced(
        orderId, order!.Number, order.BuyerUserId, order.Total, order.Currency, DateTime.Now));

    return (await orders.FindWithItemsAsync(orderId))!.ReceiptDocumentKey;
  }
}
