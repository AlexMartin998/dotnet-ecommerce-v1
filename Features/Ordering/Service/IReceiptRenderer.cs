using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>
/// Convierte una orden en el documento imprimible que se entrega al comprador.
/// </summary>
/// <remarks>
/// <para>
/// Es un puerto para que la librería de PDF sea <b>sustituible</b>. Ni el consumidor que
/// lo dispara, ni el controller que lo sirve, ni la orden saben con qué se dibuja: cambiar
/// de QuestPDF a otra cosa —o generar HTML, o una factura electrónica firmada— es escribir
/// otra implementación y cambiar una línea de registro.
/// </para>
/// <para>
/// Devuelve un <see cref="Stream"/> y no <c>byte[]</c>: quien lo recibe lo copia
/// directamente al almacén sin materializar el documento entero en memoria, y esa decisión
/// deja de ser gratis en cuanto se generen miles.
/// </para>
/// </remarks>
public interface IReceiptRenderer
{
  /// <summary>Tipo MIME de lo que produce (<c>application/pdf</c>).</summary>
  string ContentType { get; }

  /// <summary>Dibuja el comprobante de una orden.</summary>
  /// <remarks>
  /// La orden llega con sus líneas ya cargadas. <b>Todo lo que se imprime sale de ella</b>
  /// —precios, nombres, datos del cliente—, que es lo que hace que el documento sea
  /// reproducible aunque el catálogo haya cambiado desde entonces.
  /// </remarks>
  /// <param name="order">La orden con sus <c>Items</c>.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<Stream> RenderAsync(Order order, CancellationToken ct = default);
}
