using System.Text;
using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Messaging;
using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Documents;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ApiEcommerce.Tests.Features.Ordering;


/// <summary>
/// El efecto de <c>order.placed</c>: dibujar el comprobante, guardarlo y dejar la orden
/// apuntando a él.
/// </summary>
/// <remarks>
/// El efecto vive fuera del consumidor, así que «¿qué pasa si el almacén falla?» se puede
/// preguntar con un mock y sin broker delante.
/// </remarks>
public class ReceiptGeneratorTests
{
  private static Order AnOrder(string? receiptKey = null) => new()
  {
    Id = 7,
    Number = "ORD-2026-000007",
    BuyerUserId = "user-1",
    CustomerName = "Adrian",
    Subtotal = 10m,
    Total = 10m,
    ReceiptDocumentKey = receiptKey,
    Items = [new OrderItem { Sku = "SKU-1", Name = "Producto", UnitPrice = 10m, Quantity = 1, LineTotal = 10m }]
  };

  private static OrderPlaced AnEvent() => new(7, "ORD-2026-000007", "user-1", 10m, "USD", DateTime.Now);

  private static Mock<IReceiptRenderer> ARenderer()
  {
    var renderer = new Mock<IReceiptRenderer>();

    renderer.SetupGet(r => r.ContentType).Returns("application/pdf");
    renderer.Setup(r => r.RenderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.4")));

    return renderer;
  }

  private static ReceiptGenerator Sut(
      Mock<IOrderRepository> orders, Mock<IDocumentStore> documents, Mock<IReceiptRenderer>? renderer = null)
      => new(orders.Object, (renderer ?? ARenderer()).Object, documents.Object,
             NullLogger<ReceiptGenerator>.Instance);

  // ---- camino feliz --------------------------------------------------------

  [Fact]
  public async Task HandleAsync_StoresTheDocumentAndPointsTheOrderAtIt()
  {
    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(AnOrder());

    var documents = new Mock<IDocumentStore>();
    documents.Setup(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new DocumentReference("2026/09/abc.pdf", 1234));

    await Sut(orders, documents).HandleAsync(AnEvent());

    // Se persiste la clave del almacén y no una ruta de disco, para no atar las filas a
    // la infraestructura de hoy.
    orders.Verify(r => r.SetReceiptAsync(7, "2026/09/abc.pdf", It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task HandleAsync_NamesTheDocumentAfterTheOrder()
  {
    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(AnOrder());

    DocumentContent? stored = null;

    var documents = new Mock<IDocumentStore>();
    documents.Setup(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()))
             .Callback<DocumentContent, CancellationToken>((content, _) => stored = content)
             .ReturnsAsync(new DocumentReference("k", 1));

    await Sut(orders, documents).HandleAsync(AnEvent());

    Assert.Equal("ORD-2026-000007.pdf", stored!.FileName);
    Assert.Equal("application/pdf", stored.ContentType);
  }

  [Fact]
  public async Task HandleAsync_RendersFromTheDatabaseAndNotFromTheMessage()
  {
    // Dibujar con lo que viaja en el mensaje daría dos fuentes de verdad, y la del mensaje
    // puede haber quedado obsoleta esperando en la cola.
    var order = AnOrder();

    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(order);

    var documents = new Mock<IDocumentStore>();
    documents.Setup(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new DocumentReference("k", 1));

    var renderer = ARenderer();

    await Sut(orders, documents, renderer).HandleAsync(AnEvent());

    renderer.Verify(r => r.RenderAsync(order, It.IsAny<CancellationToken>()), Times.Once);
  }

  // ---- no repetir el trabajo -----------------------------------------------

  [Fact]
  public async Task HandleAsync_WhenTheOrderAlreadyHasAReceipt_DoesNothing()
  {
    // No es redundante con el inbox, que deduplica por MessageId: un replay manual desde
    // la DLQ llega con otro id y dejaría el PDF anterior huérfano.
    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>()))
          .ReturnsAsync(AnOrder(receiptKey: "2026/09/ya-existe.pdf"));

    var documents = new Mock<IDocumentStore>();
    var renderer = ARenderer();

    await Sut(orders, documents, renderer).HandleAsync(AnEvent());

    renderer.Verify(r => r.RenderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
    documents.Verify(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()), Times.Never);
    orders.Verify(r => r.SetReceiptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task HandleAsync_WhenTheOrderIsGone_DoesNotThrow()
  {
    // Reintentar no la hará aparecer: lanzar gastaría los cinco intentos y acabaría en la
    // DLQ igual, con cinco trazas de error por el camino.
    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);

    var documents = new Mock<IDocumentStore>();

    await Sut(orders, documents).HandleAsync(AnEvent());

    documents.Verify(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  // ---- fallos --------------------------------------------------------------

  [Fact]
  public async Task HandleAsync_WhenTheStoreFails_PropagatesSoTheInboxUndoesTheMark()
  {
    // Si se tragara la excepción, el inbox confirmaría la marca de «procesado» y la orden
    // se quedaría sin comprobante para siempre y sin reintento.
    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(AnOrder());

    var documents = new Mock<IDocumentStore>();
    documents.Setup(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()))
             .ThrowsAsync(new IOException("disco lleno"));

    var boom = await Assert.ThrowsAsync<IOException>(() => Sut(orders, documents).HandleAsync(AnEvent()));

    Assert.Equal("disco lleno", boom.Message);
    orders.Verify(r => r.SetReceiptAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task HandleAsync_WhenTheRendererFails_DoesNotStoreAnything()
  {
    var orders = new Mock<IOrderRepository>();
    orders.Setup(r => r.FindWithItemsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(AnOrder());

    var documents = new Mock<IDocumentStore>();

    var renderer = new Mock<IReceiptRenderer>();
    renderer.SetupGet(r => r.ContentType).Returns("application/pdf");
    renderer.Setup(r => r.RenderAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("no hay fuentes"));

    await Assert.ThrowsAsync<InvalidOperationException>(
        () => Sut(orders, documents, renderer).HandleAsync(AnEvent()));

    documents.Verify(d => d.SaveAsync(It.IsAny<DocumentContent>(), It.IsAny<CancellationToken>()), Times.Never);
  }
}
