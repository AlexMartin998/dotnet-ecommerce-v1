using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>
/// Convierte una orden en el documento imprimible que se entrega al comprador.
/// </summary>
/// <remarks>
/// Puerto para que la librería de PDF sea sustituible sin tocar al consumidor ni al
/// controller. Devuelve un <see cref="Stream"/> y no <c>byte[]</c> para que el documento se
/// copie al almacén sin materializarlo entero en memoria.
/// </remarks>
public interface IReceiptRenderer
{
  /// <summary>Tipo MIME de lo que produce (<c>application/pdf</c>).</summary>
  string ContentType { get; }

  /// <summary>Dibuja el comprobante de una orden.</summary>
  /// <remarks>
  /// La orden llega con sus líneas cargadas y todo lo que se imprime sale de ella, lo que
  /// hace el documento reproducible aunque el catálogo haya cambiado.
  /// </remarks>
  /// <param name="order">La orden con sus <c>Items</c>.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<Stream> RenderAsync(Order order, CancellationToken ct = default);
}
