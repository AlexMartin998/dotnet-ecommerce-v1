using System.Text;
using ApiEcommerce.Features.Ordering.Documents;
using ApiEcommerce.Features.Ordering.Models;
using QuestPDF.Infrastructure;

namespace ApiEcommerce.Tests.Features.Ordering;


/// <summary>El render del comprobante. Genera un PDF de verdad, no comprueba llamadas.</summary>
/// <remarks>
/// Es lo que cubre la dependencia nativa: en Linux QuestPDF dibuja con SkiaSharp, que
/// necesita <c>libfontconfig1</c>, y sin ella el fallo solo aparecería dentro del
/// contenedor y mensaje a mensaje. La licencia se declara aquí porque lanza al generar.
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

    // Comprobar solo "hay bytes" dejaría pasar un stream vacío o el volcado de un error.
    Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    Assert.True(bytes.Length > 1000, $"El PDF pesa {bytes.Length} bytes, sospechosamente poco");
  }

  [Fact]
  public async Task RenderAsync_ReturnsTheStreamRewound()
  {
    // Un stream sin rebobinar se copia al almacén como un fichero de cero bytes sin dar
    // ningún error: la orden diría "disponible" y la descarga daría un PDF vacío.
    await using var pdf = await new QuestPdfReceiptRenderer().RenderAsync(AnOrder());

    Assert.Equal(0, pdf.Position);
    Assert.True(pdf.Length > 0);
  }

  [Fact]
  public async Task RenderAsync_EmbedsItsOwnFontSoTheOutputDoesNotDependOnTheMachine()
  {
    // Con una fuente del sistema, dos réplicas producirían comprobantes distintos para la
    // misma orden; Lato viene embebida en el paquete.
    var bytes = await RenderAsync(AnOrder());
    var raw = Encoding.Latin1.GetString(bytes);

    Assert.Contains("Lato", raw);
    Assert.Contains("FontFile", raw);
  }

  [Fact]
  public async Task RenderAsync_WithManyLines_StillProducesOneDocument()
  {
    // Un pedido largo pagina, y la cabecera se declara como Header para que se repita.
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
