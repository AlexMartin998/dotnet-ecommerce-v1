using System.Text;
using ApiEcommerce.Features.Ordering.Documents;
using ApiEcommerce.Features.Ordering.Models;
using QuestPDF.Infrastructure;

namespace ApiEcommerce.Tests.Features.Ordering;


/// <summary>
/// El render del comprobante. <b>Genera un PDF de verdad</b>, no comprueba llamadas.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Este es el test que cubre la dependencia nativa.</b> En Linux QuestPDF dibuja con
/// SkiaSharp, que necesita <c>libfontconfig1</c>: sin ella, esto revienta. Y ese es
/// justamente el fallo que de otra forma solo aparecería <i>dentro del contenedor</i> y
/// mensaje a mensaje, con cinco reintentos y una DLQ por comprobante.
/// </para>
/// <para>
/// La licencia se declara aquí igual que en el arranque real: QuestPDF <b>lanza al
/// generar</b>, no al registrar, así que sin esta línea el test fallaría por un motivo que
/// no tiene nada que ver con lo que prueba.
/// </para>
/// </remarks>
public class QuestPdfReceiptRendererTests
{
  static QuestPdfReceiptRendererTests() => QuestPDF.Settings.License = LicenseType.Community;

  private static Order AnOrder(int lines = 2) => new()
  {
    Id = 1,
    Number = "ORD-2026-000042",
    BuyerUserId = "user-1",
    Status = OrderStatus.Paid,
    Currency = "USD",
    CustomerName = "Adrián Martín",
    CustomerEmail = "adrian@test.local",
    CustomerPhone = "+593999999999",
    ShippingAddress = "Av. Siempre Viva 742, Quito",
    Subtotal = 10m * lines,
    Total = 10m * lines,
    PlacedAt = new DateTime(2026, 9, 6, 20, 43, 0),
    Items = [.. Enumerable.Range(1, lines).Select(i => new OrderItem
    {
      ProductId = i,
      Sku = $"SKU-{i:D4}",
      Name = $"Producto {i}",
      UnitPrice = 10m,
      Quantity = 1,
      LineTotal = 10m
    })]
  };

  private static async Task<byte[]> RenderAsync(Order order)
  {
    await using var pdf = await new QuestPdfReceiptRenderer().RenderAsync(order);

    using var buffer = new MemoryStream();
    await pdf.CopyToAsync(buffer);

    return buffer.ToArray();
  }

  [Fact]
  public async Task RenderAsync_ProducesARealPdf()
  {
    var bytes = await RenderAsync(AnOrder());

    // La firma de un PDF. Comprobar solo "hay bytes" dejaría pasar un stream vacío o el
    // volcado de una excepción.
    Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    Assert.True(bytes.Length > 1000, $"El PDF pesa {bytes.Length} bytes, sospechosamente poco");
  }

  [Fact]
  public async Task RenderAsync_ReturnsTheStreamRewound()
  {
    // ⚠️ Un stream en la última posición se copia al almacén como un fichero de CERO
    // bytes, sin un solo error: la orden diría "comprobante disponible" y la descarga
    // daría un PDF vacío.
    await using var pdf = await new QuestPdfReceiptRenderer().RenderAsync(AnOrder());

    Assert.Equal(0, pdf.Position);
    Assert.True(pdf.Length > 0);
  }

  [Fact]
  public async Task RenderAsync_EmbedsItsOwnFontSoTheOutputDoesNotDependOnTheMachine()
  {
    // Es la razón de no pedir Calibri: QuestPDF embebe Lato en el paquete, así que el
    // documento sale igual en local y en la imagen. Con una fuente del sistema, dos
    // réplicas podrían producir comprobantes distintos para la misma orden.
    var bytes = await RenderAsync(AnOrder());
    var raw = Encoding.Latin1.GetString(bytes);

    Assert.Contains("Lato", raw);
    Assert.Contains("FontFile", raw);
  }

  [Fact]
  public async Task RenderAsync_WithManyLines_StillProducesOneDocument()
  {
    // Un pedido largo pagina; lo que no puede es fallar. La cabecera de la tabla se
    // declara como Header precisamente para que se repita en cada página.
    var bytes = await RenderAsync(AnOrder(lines: 80));

    Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
  }

  [Fact]
  public async Task RenderAsync_WithoutOptionalCustomerData_DoesNotFail()
  {
    var order = AnOrder();
    order.CustomerEmail = null;
    order.CustomerPhone = null;
    order.ShippingAddress = null;

    var bytes = await RenderAsync(order);

    Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
  }

  [Fact]
  public async Task RenderAsync_WhenCancelled_Throws()
  {
    using var cancelled = new CancellationTokenSource();
    await cancelled.CancelAsync();

    await Assert.ThrowsAsync<OperationCanceledException>(
        () => new QuestPdfReceiptRenderer().RenderAsync(AnOrder(), cancelled.Token));
  }
}
