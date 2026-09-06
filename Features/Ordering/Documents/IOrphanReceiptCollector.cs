namespace ApiEcommerce.Features.Ordering.Documents;


/// <summary>
/// Borra del almacén los comprobantes que <b>ninguna orden referencia</b>. El
/// <b>efecto</b>, separado del temporizador.
/// </summary>
/// <remarks>
/// <para>
/// Está fuera del <c>BackgroundService</c> por la lección de <c>planning/18</c>: lo que vive
/// dentro de uno no se puede probar. Y aquí eso importa más que en ningún otro sitio,
/// porque este componente <b>borra ficheros</b>: los tests que de verdad hacen falta son
/// «no borra el referenciado» y «no borra el recién escrito», y ninguno se puede escribir
/// contra un job con un temporizador de horas dentro.
/// </para>
/// <para>
/// Existe porque el comprobante se escribe <b>dentro</b> de la transacción del inbox y un
/// fichero no se deshace con ella: si el commit falla, queda un PDF que nadie apunta. Se
/// aceptó a sabiendas y en esa dirección —un huérfano es basura recolectable, un
/// comprobante perdido es un cliente sin su documento—, pero alguien tiene que recogerla.
/// </para>
/// </remarks>
public interface IOrphanReceiptCollector
{
  /// <summary>Hace una pasada completa y devuelve cuántos documentos borró.</summary>
  Task<int> CollectAsync(CancellationToken ct = default);
}
