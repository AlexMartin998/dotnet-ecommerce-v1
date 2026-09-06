using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Ordering.Events;


/// <summary>
/// Se publica cuando una orden quedó registrada y cobrada.
/// </summary>
/// <remarks>
/// <para>
/// Es el disparador de la generación del comprobante. Y por eso se emite por el
/// <b>outbox</b> y no llamando al generador: escribir el evento es parte de la
/// transacción de la compra, así que o hay orden y evento, o no hay ninguno de los dos.
/// Llamar al generador aquí ataría la compra a que el generador esté vivo — y generar un
/// PDF tarda lo suyo.
/// </para>
/// <para>
/// ⚠️ Lleva <b>solo el id y el número</b>, no la orden entera. Es la excepción consciente
/// a la regla de "un evento debe traer lo que el consumidor necesita": aquí el consumidor
/// vive en el mismo proceso y necesita el <i>estado confirmado</i> de la orden con sus
/// líneas. Meter todo el detalle en el mensaje duplicaría la fuente de verdad —el
/// documento se generaría a partir de una copia que puede haber quedado obsoleta— y haría
/// el payload grande sin ganar nada. El día que el generador sea otro servicio, lo que
/// hay que añadir es el detalle, no cambiar el mecanismo.
/// </para>
/// </remarks>
public sealed record OrderPlaced(
    int OrderId,
    string OrderNumber,
    string BuyerUserId,
    decimal Total,
    string Currency,
    DateTime OccurredAt) : IDomainEvent
{
  public static string EventType => "order.placed";
}
