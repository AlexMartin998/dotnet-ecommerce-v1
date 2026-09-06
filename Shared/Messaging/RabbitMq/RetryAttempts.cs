using System.Text;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// El contador de intentos de un mensaje, en una cabecera <b>nuestra</b>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Nuestro y no <c>x-death</c>, a propósito.</b> Leerlo del broker tenía dos
/// problemas, y los dos eran reales:
/// </para>
/// <para>
/// 1) <b>Un replay desde la DLQ no reseteaba el presupuesto.</b> <c>x-death</c> sobrevive
/// al paso por la DLQ, así que un mensaje que un operador reencolaba volvía con el
/// contador ya agotado y moría en la primera entrega: la herramienta que existe para
/// recuperar mensajes no los recuperaba. Con una cabecera nuestra, el procedimiento de
/// replay es <b>borrar esta cabecera</b> — una, con nombre conocido.
/// </para>
/// <para>
/// 2) <b>El parseo era frágil.</b> <c>x-death</c> es una lista de diccionarios cuyos
/// valores de texto viajan como <c>byte[]</c>: compararlos con un <c>string</c> sin
/// convertir devuelve <c>false</c> en silencio (ya pasó). Y había que filtrar por el
/// NOMBRE de la cola de reintento, que ahora lleva el TTL dentro para poder cambiarlo sin
/// un 406 — o sea que el contador se habría reseteado solo al cambiar el plazo.
/// </para>
/// <para>
/// Es una función pura y vive fuera del consumidor para poder probarla sin broker. Es la
/// misma razón por la que el efecto y la unidad transaccional también salieron de ahí.
/// </para>
/// </remarks>
public static class RetryAttempts
{
  /// <summary>Nombre de la cabecera. Borrarla es lo que reinicia el presupuesto.</summary>
  public const string HeaderName = "x-retry-attempt";

  /// <summary>Intentos ya gastados por este mensaje.</summary>
  /// <remarks>
  /// Acepta los tres formatos en los que puede llegar el valor: el <c>int</c> que
  /// escribimos nosotros, y el texto —crudo o como <c>byte[]</c>— de alguien que la haya
  /// puesto a mano desde la UI del broker. Un valor ilegible cuenta como cero: preferible
  /// un reintento de más que descartar un mensaje por no saber leer una cabecera.
  /// </remarks>
  /// <param name="headers">Cabeceras del mensaje entrante, o <c>null</c>.</param>
  public static int Read(IDictionary<string, object?>? headers)
  {
    if (headers?.TryGetValue(HeaderName, out var raw) is not true) return 0;

    var attempts = raw switch
    {
      int value => value,
      long value => (int)value,
      byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
      string text when int.TryParse(text, out var parsed) => parsed,
      _ => 0
    };

    // Un valor negativo solo puede venir de alguien tocándolo a mano, y daría un
    // presupuesto infinito de reintentos.
    return attempts < 0 ? 0 : attempts;
  }

  /// <summary>
  /// Copia las cabeceras del mensaje y deja el contador en <paramref name="attempts"/>.
  /// </summary>
  /// <remarks>
  /// Copia y no muta: el diccionario de entrada es del mensaje que estamos consumiendo, y
  /// reutilizarlo tal cual acopla lo que publicamos a lo que recibimos.
  /// </remarks>
  /// <param name="headers">Cabeceras del mensaje entrante, o <c>null</c>.</param>
  /// <param name="attempts">Intentos gastados, contando el que acaba de fallar.</param>
  public static Dictionary<string, object?> With(IDictionary<string, object?>? headers, int attempts)
  {
    var copy = headers is null
        ? new Dictionary<string, object?>()
        : new Dictionary<string, object?>(headers);

    copy[HeaderName] = attempts;

    return copy;
  }
}
