using System.Globalization;
using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Service;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ApiEcommerce.Features.Ordering.Documents;


/// <summary>Dibuja el comprobante con QuestPDF.</summary>
/// <remarks>
/// Dos trampas que fallan en ejecución y no al arrancar: <c>QuestPDF.Settings.License</c> se
/// declara en <c>AddReceiptRendering</c>, y en Linux SkiaSharp necesita <c>libfontconfig1</c>
/// y fuentes instaladas (están en el <c>Dockerfile</c>).
/// </remarks>
public sealed class QuestPdfReceiptRenderer : IReceiptRenderer
{
  public string ContentType => "application/pdf";

  /// <summary>Cultura fija para el documento.</summary>
  /// <remarks>
  /// Invariante y no la del servidor: con la cultura ambiente, dos réplicas producirían
  /// separadores decimales distintos para la misma orden.
  /// </remarks>
  private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

  public Task<Stream> RenderAsync(Order order, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(order);

    ct.ThrowIfCancellationRequested();

    var stream = new MemoryStream();

    Document.Create(document =>
    {
      document.Page(page =>
      {
        page.Size(PageSizes.A4);
        page.Margin(2, Unit.Centimetre);
        // Sin FontFamily explícita: QuestPDF embebe Lato, y pedir una fuente del sistema
        // dejaría el documento a merced de la sustitución de cada máquina.
        page.DefaultTextStyle(text => text.FontSize(10));

        page.Header().Element(container => Header(container, order));
        page.Content().Element(container => Content(container, order));

        // El pie va en todas las páginas: sin numerarlas no se sabe si el comprobante está completo.
        page.Footer().AlignCenter().Text(text =>
        {
          text.DefaultTextStyle(style => style.FontSize(8).FontColor(Colors.Grey.Medium));
          text.Span($"{order.Number}  ·  ");
          text.CurrentPageNumber();
          text.Span(" / ");
          text.TotalPages();
        });
      });
    }).GeneratePdf(stream);

    // Quien lo recibe lo copia al almacén: sin rebobinar guardaría cero bytes y sin error.
    stream.Position = 0;

    return Task.FromResult<Stream>(stream);
  }

  private static void Header(IContainer container, Order order) =>
      container.Column(column =>
      {
        column.Item().Row(row =>
        {
          row.RelativeItem().Column(left =>
          {
            left.Item().Text("COMPROBANTE").FontSize(18).Bold();
            left.Item().Text(order.Number).FontSize(13).FontColor(Colors.Grey.Darken2);
          });

          row.ConstantItem(180).AlignRight().Column(right =>
          {
            right.Item().AlignRight().Text(order.PlacedAt.ToString("dd MMM yyyy · HH:mm", Culture));
            right.Item().AlignRight().Text(order.Status.ToString().ToUpperInvariant())
                 .Bold().FontColor(Colors.Green.Darken2);
            right.Item().AlignRight().Text(order.Currency).FontColor(Colors.Grey.Darken1);
          });
        });

        column.Item().PaddingVertical(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
      });

  private static void Content(IContainer container, Order order) =>
      container.PaddingTop(6).Column(column =>
      {
        column.Spacing(14);

        column.Item().Element(inner => Customer(inner, order));
        column.Item().Element(inner => Items(inner, order));
        column.Item().AlignRight().Element(inner => Totals(inner, order));
      });

  private static void Customer(IContainer container, Order order) =>
      container.Row(row =>
      {
        row.RelativeItem().Column(left =>
        {
          left.Item().Text("CLIENTE").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
          left.Item().Text(order.CustomerName).SemiBold();

          if (!string.IsNullOrWhiteSpace(order.CustomerEmail)) left.Item().Text(order.CustomerEmail);
          if (!string.IsNullOrWhiteSpace(order.CustomerPhone)) left.Item().Text(order.CustomerPhone);
        });

        row.RelativeItem().Column(right =>
        {
          right.Item().Text("ENVÍO").FontSize(8).Bold().FontColor(Colors.Grey.Darken1);
          right.Item().Text(order.ShippingAddress ?? "—");
        });
      });

  private static void Items(IContainer container, Order order) =>
      container.Table(table =>
      {
        table.ColumnsDefinition(columns =>
        {
          columns.RelativeColumn(4);    // producto
          columns.RelativeColumn(2);    // sku
          columns.ConstantColumn(45);   // cantidad
          columns.ConstantColumn(75);   // precio
          columns.ConstantColumn(80);   // total
        });

        // `Header` y no una fila normal: QuestPDF la repite en cada página del pedido.
        table.Header(header =>
        {
          header.Cell().Element(HeaderCell).Text("ITEM");
          header.Cell().Element(HeaderCell).Text("SKU");
          header.Cell().Element(HeaderCell).AlignRight().Text("CANT.");
          header.Cell().Element(HeaderCell).AlignRight().Text("PRECIO");
          header.Cell().Element(HeaderCell).AlignRight().Text("TOTAL");
        });

        foreach (var item in order.Items)
        {
          table.Cell().Element(BodyCell).Text(item.Name);
          table.Cell().Element(BodyCell).Text(item.Sku).FontColor(Colors.Grey.Darken1);
          table.Cell().Element(BodyCell).AlignRight().Text(item.Quantity.ToString(Culture));
          table.Cell().Element(BodyCell).AlignRight().Text(Money(item.UnitPrice));
          table.Cell().Element(BodyCell).AlignRight().Text(Money(item.LineTotal));
        }

        static IContainer HeaderCell(IContainer cell) => cell
            .BorderBottom(1).BorderColor(Colors.Grey.Darken1)
            .PaddingVertical(5)
            .DefaultTextStyle(text => text.FontSize(8).Bold().FontColor(Colors.Grey.Darken2));

        static IContainer BodyCell(IContainer cell) => cell
            .BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(6);
      });

  private static void Totals(IContainer container, Order order) =>
      container.Width(230).Column(column =>
      {
        Line(column, "Subtotal", order.Subtotal);

        // Solo se imprimen si existen: una fila "Descuento 0,00" es ruido.
        if (order.Discount != 0m) Line(column, "Descuento", -order.Discount);
        if (order.Tax != 0m) Line(column, "Impuestos", order.Tax);
        if (order.Shipping != 0m) Line(column, "Envío", order.Shipping);

        column.Item().PaddingVertical(5).LineHorizontal(1).LineColor(Colors.Grey.Darken1);

        column.Item().Row(row =>
        {
          row.RelativeItem().Text("TOTAL").Bold();
          row.ConstantItem(110).AlignRight()
             .Text($"{Money(order.Total)} {order.Currency}").Bold().FontSize(12);
        });

        static void Line(ColumnDescriptor column, string label, decimal amount) =>
            column.Item().Row(row =>
            {
              row.RelativeItem().Text(label).FontColor(Colors.Grey.Darken2);
              row.ConstantItem(110).AlignRight().Text(Money(amount));
            });
      });

  private static string Money(decimal amount) => amount.ToString("N2", Culture);
}
