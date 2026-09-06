namespace ApiEcommerce.Features.Ordering.Documents;


/// <summary>
/// Borra del almacén los comprobantes que ninguna orden referencia. El efecto, separado
/// del temporizador que lo dispara.
/// </summary>
/// <remarks>
/// Los huérfanos existen porque el PDF se escribe dentro de la transacción del inbox y un
/// fichero no se deshace con ella. Está fuera del <c>BackgroundService</c> para poder
/// probar que no borra ni el referenciado ni el recién escrito.
/// </remarks>
public interface IOrphanReceiptCollector
{
  /// <summary>Hace una pasada completa y devuelve cuántos documentos borró.</summary>
  Task<int> CollectAsync(CancellationToken ct = default);
}
