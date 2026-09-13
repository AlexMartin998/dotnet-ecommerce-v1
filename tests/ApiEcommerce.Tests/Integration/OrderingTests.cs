using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Messaging;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Órdenes y su comprobante, contra la API entera.
/// </summary>
/// <remarks>
/// El host de tests corre sin broker, así que nadie drena el outbox: la compra deja el
/// comprobante <c>pending</c> y la generación se dispara llamando al efecto
/// (<see cref="IReceiptGenerator"/>), que es la mitad determinista.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class OrderingTests(ApiFactory factory)
{
  // ---- la orden ------------------------------------------------------------

  [Fact]
  public async Task PlacingAnOrderFreezesThePriceAndTheName()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 2)]);

    Assert.StartsWith($"ORD-{DateTime.Now:yyyy}-", order.GetProperty("number").GetString());
    Assert.Equal("pending", order.GetProperty("receiptStatus").GetString());

    var item = order.GetProperty("items")[0];

    // Copiados, no referenciados: el comprobante de ayer tiene que seguir diciendo lo que
    // se cobró de verdad aunque el catálogo cambie.
    Assert.Equal(sku, item.GetProperty("sku").GetString());
    Assert.Equal(9.99m, item.GetProperty("unitPrice").GetDecimal());
    Assert.Equal(19.98m, item.GetProperty("lineTotal").GetDecimal());
    Assert.Equal(19.98m, order.GetProperty("total").GetDecimal());
  }

  [Fact]
  public async Task PlacingAnOrderDecrementsStockInTheSameTransaction()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    await PlaceAsync(user, [(sku, 3)]);

    Assert.Equal(7, await StockOf(admin, sku));
  }

  [Fact]
  public async Task RepeatedLinesAreMergedIntoOne()
  {
    // Sin agrupar, el mismo SKU dos veces deja dos líneas idénticas en el comprobante.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 2), (sku, 3)]);

    Assert.Equal(1, order.GetProperty("items").GetArrayLength());
    Assert.Equal(5, order.GetProperty("items")[0].GetProperty("quantity").GetInt32());
    Assert.Equal(5, await StockOf(admin, sku));
  }

  [Fact]
  public async Task WithoutEnoughStockThereIsNoOrderAtAll()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 1);

    using var user = await factory.AsNewUserAsync();

    var response = await user.PostAsJsonAsync("/api/v1/order", Body([(sku, 5)]));

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

    // Y no queda media orden: la transacción se deshizo entera.
    var mine = await user.GetFromJsonAsync<JsonElement>("/api/v1/order/paged");
    Assert.Equal(0, mine.GetProperty("totalItems").GetInt32());
    Assert.Equal(1, await StockOf(admin, sku));
  }

  [Fact]
  public async Task AMissingSkuIsIndistinguishableFromNoStock()
  {
    // Distinguirlos convertiría el checkout en un inventario consultable desde fuera.
    using var user = await factory.AsNewUserAsync();

    var response = await user.PostAsJsonAsync("/api/v1/order", Body([("NO-EXISTE", 1)]));

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task TheSameIdempotencyKeyReturnsTheSameOrder()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var first = await PlaceAsync(user, [(sku, 2)], key);
    var second = await PlaceAsync(user, [(sku, 2)], key);

    Assert.Equal(first.PublicId(), second.PublicId());

    // Lo que de verdad importa no es el id repetido, es que solo se cobró una vez.
    Assert.Equal(8, await StockOf(admin, sku));

    var mine = await user.GetFromJsonAsync<JsonElement>("/api/v1/order/paged");
    Assert.Equal(1, mine.GetProperty("totalItems").GetInt32());
  }

  // ---- de quién es cada orden ---------------------------------------------

  [Fact]
  public async Task SomeoneElsesOrderIs404AndNot403()
  {
    // 403 diría "existe pero no es tuya", y con ids correlativos eso permite contar las
    // órdenes de la tienda desde fuera.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var owner = await factory.AsNewUserAsync();
    var order = await PlaceAsync(owner, [(sku, 1)]);
    var publicId = order.PublicId();

    using var stranger = await factory.AsNewUserAsync();

    Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/order/{publicId}")).StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/v1/order/{publicId}/receipt")).StatusCode);
  }

  [Fact]
  public async Task TheOrderIsIdentifiedByItsPublicIdAndNeverByItsPrimaryKey()
  {
    // Con la clave primaria en la ruta basta con sumar uno para descubrir órdenes.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    using var response = await user.PostAsJsonAsync("/api/v1/order", Body([(sku, 1)]));
    var order = await response.Content.ReadFromJsonAsync<JsonElement>();
    var publicId = order.PublicId();

    Assert.False(order.TryGetProperty("id", out _));
    Assert.EndsWith($"/order/{publicId}", response.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/v1/order/{publicId}")).StatusCode);

    // La clave primaria ni siquiera casa con la ruta: la restricción es :guid.
    var id = await factory.OrderIdAsync(publicId);
    Assert.Equal(HttpStatusCode.NotFound, (await user.GetAsync($"/api/v1/order/{id}")).StatusCode);
  }

  [Fact]
  public async Task WithoutATokenThereIsNoOrderAndNoReceipt()
  {
    using var anonymous = factory.Anonymous();

    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/order/{Guid.NewGuid()}")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/order/{Guid.NewGuid()}/receipt")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await anonymous.PostAsJsonAsync("/api/v1/order", Body([("X", 1)]))).StatusCode);
  }

  [Fact]
  public async Task EveryoneOnlySeesTheirOwnOrders()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var mine = await factory.AsNewUserAsync();
    await PlaceAsync(mine, [(sku, 1)]);

    using var theirs = await factory.AsNewUserAsync();
    await PlaceAsync(theirs, [(sku, 1)]);

    var page = await mine.GetFromJsonAsync<JsonElement>("/api/v1/order/paged");

    Assert.Equal(1, page.GetProperty("totalItems").GetInt32());
  }

  // ---- el comprobante ------------------------------------------------------

  [Fact]
  public async Task BuyingDoesNotWaitForThePdf()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);

    // La orden ya existe y el comprobante todavía no: es el estado que hace falta poder
    // distinguir, y por eso ReceiptStatus es una columna y no un booleano derivado.
    Assert.Equal("pending", order.GetProperty("receiptStatus").GetString());
  }

  [Fact]
  public async Task AReceiptThatIsNotReadyIs409WithARetryableCode()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var publicId = (await PlaceAsync(user, [(sku, 1)])).PublicId();

    var response = await user.GetAsync($"/api/v1/order/{publicId}/receipt");

    // 409 y no 404: el documento EXISTIRÁ. Un 404 le diría al cliente que deje de pedirlo.
    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("receipt_not_ready", problem.GetProperty("code").GetString());
  }

  [Fact]
  public async Task OnceGeneratedTheReceiptDownloadsAsAPdfNamedAfterTheOrder()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 2)]);
    var publicId = order.PublicId();
    var id = await factory.OrderIdAsync(publicId);
    var number = order.GetProperty("number").GetString();

    await GenerateReceiptAsync(id, number!);

    // Ahora la orden lo dice, que es lo que el cliente consulta para saber si puede bajar.
    var refreshed = await user.GetFromJsonAsync<JsonElement>($"/api/v1/order/{publicId}");
    Assert.Equal("available", refreshed.GetProperty("receiptStatus").GetString());

    // Y la clave del documento NO se expone: es un detalle del almacén.
    Assert.False(refreshed.TryGetProperty("receiptDocumentKey", out _));

    var response = await user.GetAsync($"/api/v1/order/{publicId}/receipt");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
    Assert.Contains($"{number}.pdf", response.Content.Headers.ContentDisposition?.ToString());

    var bytes = await response.Content.ReadAsByteArrayAsync();

    Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    Assert.True(bytes.Length > 1000);
  }

  [Fact]
  public async Task GeneratingTheReceiptTwiceDoesNotReplaceIt()
  {
    // El inbox deduplica por MessageId; esto cubre el otro camino, el replay manual desde
    // la DLQ, que llega con un id nuevo y por tanto pasa el filtro del inbox.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);
    var publicId = order.PublicId();
    var id = await factory.OrderIdAsync(publicId);
    var number = order.GetProperty("number").GetString()!;

    await GenerateReceiptAsync(id, number);
    var first = await DocumentKeyOf(id);

    await GenerateReceiptAsync(id, number);
    var second = await DocumentKeyOf(id);

    Assert.Equal(first, second);
  }

  [Fact]
  public async Task TheStoredKeyIsNotAPathIntoOurDirectoryTree()
  {
    // Guardar "/app/App_Data/documents/2026/09/x.pdf" ataría la base a la infraestructura
    // de hoy: al migrar a S3 habría que reescribir todas las filas.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);

    var id = await factory.OrderIdAsync(order.PublicId());

    await GenerateReceiptAsync(id, order.GetProperty("number").GetString()!);

    var key = await DocumentKeyOf(id);

    Assert.NotNull(key);
    Assert.DoesNotContain("App_Data", key);
    Assert.DoesNotContain("wwwroot", key);
    Assert.False(Path.IsPathRooted(key));
  }


  // ---- el comprobante, DENTRO de la transacción del inbox --------------------

  [Fact]
  public async Task AFailingReceiptLeavesNoMarkAndNoKey()
  {
    // Si la marca sobreviviera a un efecto fallido, la reentrega daría el mensaje por
    // procesado y el comprobante no se generaría nunca.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);
    var publicId = order.PublicId();
    var id = await factory.OrderIdAsync(publicId);

    var messageId = Guid.NewGuid();

    using var scope = factory.Services.CreateScope();
    var inbox = scope.ServiceProvider.GetRequiredService<IMessageInbox>();

    await Assert.ThrowsAsync<IOException>(() => inbox.ProcessOnceAsync(
        messageId, OrderPaid.EventType, _ => throw new IOException("el almacén falló")));

    Assert.Null(await DocumentKeyOf(id));
    Assert.False(await IsMarkedAsync(messageId));
  }

  [Fact]
  public async Task TheReceiptAndTheProcessedMarkAreCommittedTogether()
  {
    // La otra mitad: en el camino feliz sí quedan las dos cosas. Sin este, el test de
    // arriba pasaría igual con un inbox que no hiciera nada.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);
    var publicId = order.PublicId();
    var id = await factory.OrderIdAsync(publicId);
    var number = order.GetProperty("number").GetString()!;

    var messageId = Guid.NewGuid();

    using var scope = factory.Services.CreateScope();
    var inbox = scope.ServiceProvider.GetRequiredService<IMessageInbox>();
    var generator = scope.ServiceProvider.GetRequiredService<IReceiptGenerator>();

    var processed = await inbox.ProcessOnceAsync(
        messageId, OrderPaid.EventType,
        token => generator.HandleAsync(
            new OrderPaid(id, number, "irrelevante", 0m, "USD", DateTime.Now), token));

    Assert.True(processed);
    Assert.NotNull(await DocumentKeyOf(id));
    Assert.True(await IsMarkedAsync(messageId));

    // Y la reentrega del MISMO mensaje no vuelve a generar nada.
    Assert.False(await inbox.ProcessOnceAsync(
        messageId, OrderPaid.EventType, _ => throw new InvalidOperationException("no debería ejecutarse")));
  }

  [Fact]
  public async Task AReceiptThatWillNeverExistIsNotReportedAsNotReady()
  {
    // `receipt_not_ready` significa «vuelve en un momento»: devolverlo para un comprobante
    // que murió en la DLQ deja al cliente haciendo polling eterno sobre algo inexistente.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var publicId = (await PlaceAsync(user, [(sku, 1)])).PublicId();
    var id = await factory.OrderIdAsync(publicId);

    using (var scope = factory.Services.CreateScope())
      await scope.ServiceProvider.GetRequiredService<IOrderRepository>().SetReceiptFailedAsync(id);

    var refreshed = await user.GetFromJsonAsync<JsonElement>($"/api/v1/order/{publicId}");
    Assert.Equal("failed", refreshed.GetProperty("receiptStatus").GetString());

    var response = await user.GetAsync($"/api/v1/order/{publicId}/receipt");
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Equal("receipt_failed", problem.GetProperty("code").GetString());
  }

  [Fact]
  public async Task MarkingAReceiptAsFailedNeverOverwritesOneThatIsAlreadyThere()
  {
    // El aviso de «agotado» llega desde el consumidor y puede cruzarse con un replay que
    // sí terminó bien. Sin la condición en el UPDATE, marcaría como fallido un comprobante
    // que está en el almacén y descargándose.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);
    var publicId = order.PublicId();
    var id = await factory.OrderIdAsync(publicId);

    await GenerateReceiptAsync(id, order.GetProperty("number").GetString()!);

    using (var scope = factory.Services.CreateScope())
      await scope.ServiceProvider.GetRequiredService<IOrderRepository>().SetReceiptFailedAsync(id);

    var refreshed = await user.GetFromJsonAsync<JsonElement>($"/api/v1/order/{publicId}");

    Assert.Equal("available", refreshed.GetProperty("receiptStatus").GetString());
    Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/v1/order/{publicId}/receipt")).StatusCode);
  }

  [Fact]
  public async Task AnAbsurdPageNumberIs200WithAnEmptyPageAndNotA500()
  {
    // `(Page - 1) * PageSize` puede desbordar a negativo, y SQL Server responde con un 500.
    using var user = await factory.AsNewUserAsync();

    var response = await user.GetAsync("/api/v1/order/paged?page=2147483647&pageSize=100");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>())
        .GetProperty("items").GetArrayLength());
  }

  [Fact]
  public async Task TheReceiptIsNotCachedOnDisk()
  {
    // Lleva nombre, dirección e importe: en un equipo compartido no puede quedarse en la
    // cache del navegador después de cerrar sesión.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);

    await GenerateReceiptAsync(await factory.OrderIdAsync(order.PublicId()), order.GetProperty("number").GetString()!);

    var response = await user.GetAsync($"/api/v1/order/{order.PublicId()}/receipt");

    Assert.True(response.Headers.CacheControl?.NoStore);
  }

  private async Task<bool> IsMarkedAsync(Guid messageId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    return await db.ProcessedMessages.AsNoTracking().AnyAsync(m => m.Id == messageId);
  }

  // ---- listado de administración -------------------------------------------

  [Fact]
  public async Task TheAdminListingSeesOrdersOfEveryBuyer()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var order = await PlaceAsync(user, [(sku, 1)]);
    var number = order.GetProperty("number").GetString();

    // Filtrado por número y no leyendo la primera página: la base es compartida entre tests.
    var page = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/order/all?number={number}");

    Assert.Equal(1, page.GetProperty("totalItems").GetInt32());

    var listed = page.GetProperty("items")[0];

    Assert.Equal(number, listed.GetProperty("number").GetString());
    Assert.Equal(await UserIdOf(user), listed.GetProperty("buyerUserId").GetString());
  }

  [Fact]
  public async Task TheAdminListingFiltersByNumberPrefix()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var number = (await PlaceAsync(user, [(sku, 1)])).GetProperty("number").GetString()!;

    // Un prefijo del año trae varias; el número entero trae exactamente una.
    var byPrefix = await admin.GetFromJsonAsync<JsonElement>(
        $"/api/v1/order/all?number={number[..^2]}&pageSize=100");

    Assert.Contains(
        byPrefix.GetProperty("items").EnumerateArray(),
        o => o.GetProperty("number").GetString() == number);
  }

  [Fact]
  public async Task TheAdminListingRejectsARegularUser()
  {
    // Es la razón de que sea una ruta aparte: la autorización no depende de ningún filtro.
    using var user = await factory.AsNewUserAsync();

    var response = await user.GetAsync("/api/v1/order/all");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task TheBuyerListingStillHidesTheOrdersOfOthers()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var buyer = await factory.AsNewUserAsync();
    var number = (await PlaceAsync(buyer, [(sku, 1)])).GetProperty("number").GetString();

    using var otro = await factory.AsNewUserAsync();
    var page = await otro.GetFromJsonAsync<JsonElement>("/api/v1/order/paged?pageSize=100");

    Assert.Equal(0, page.GetProperty("totalItems").GetInt32());
    Assert.DoesNotContain(
        page.GetProperty("items").EnumerateArray(),
        o => o.GetProperty("number").GetString() == number);
  }

  // ---- helpers -------------------------------------------------------------

  private static async Task<string?> UserIdOf(HttpClient client)
      => (await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/me"))
          .GetProperty("id").GetString();


  private static object Body((string Sku, int Quantity)[] lines) => new
  {
    items = lines.Select(l => new { sku = l.Sku, quantity = l.Quantity }),
    customerName = "Cliente de prueba",
    customerPhone = "+593999999999",
    shippingAddress = "Av. Siempre Viva 742"
  };

  private static async Task<JsonElement> PlaceAsync(
      HttpClient client, (string Sku, int Quantity)[] lines, string? key = null)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/order")
    {
      Content = JsonContent.Create(Body(lines))
    };

    if (key is not null) request.Headers.Add("Idempotency-Key", key);

    var response = await client.SendAsync(request);

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<JsonElement>();
  }

  /// <summary>
  /// Dispara el efecto que dispararía el consumidor. Sin broker en el host de tests, esta
  /// es la mitad determinista de «llega el evento y se genera el PDF».
  /// </summary>
  private async Task GenerateReceiptAsync(int orderId, string number)
  {
    using var scope = factory.Services.CreateScope();

    await scope.ServiceProvider.GetRequiredService<IReceiptGenerator>().HandleAsync(
        new OrderPaid(orderId, number, "irrelevante", 0m, "USD", DateTime.Now));
  }

  private async Task<string?> DocumentKeyOf(int orderId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    return await db.Orders.AsNoTracking()
        .Where(o => o.Id == orderId)
        .Select(o => o.ReceiptDocumentKey)
        .FirstAsync();
  }

  /// <summary>Stock actual de un SKU. Reutiliza el de <c>IdempotencyTests</c>.</summary>
  /// <remarks>
  /// El listado paginado ordena por <c>CreatedAt</c> descendente, así que el producto
  /// recién creado entra en la primera página aunque la base arrastre los de otros tests.
  /// </remarks>
  private static Task<int> StockOf(HttpClient admin, string sku)
      => IdempotencyTests.StockOf(admin, sku);
}
