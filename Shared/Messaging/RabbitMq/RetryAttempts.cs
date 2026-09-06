using System.Text;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// El contador de intentos de un mensaje, en una cabecera <b>nuestra</b>.
/// </summary>
/// <remarks>
/// Nuestra y no <c>x-death</c>: esa sobrevive al paso por la DLQ, así que un replay volvía con el
/// presupuesto ya gastado, y su parseo es frágil (valores <c>byte[]</c> y filtrado por el nombre
/// de la cola de reintento, que lleva el TTL dentro). Es pura, y se prueba sin broker.
/// </remarks>
public static class RetryAttempts
{
  /// <summary>Nombre de la cabecera. Borrarla es lo que reinicia el presupuesto.</summary>
  public const string HeaderName = "x-retry-attempt";

  /// <summary>Intentos ya gastados por este mensaje.</summary>
  /// <remarks>
  /// Acepta los formatos en que puede llegar el valor, incluido el texto de quien la ponga a mano
  /// desde la UI del broker. Un valor ilegible cuenta como cero: mejor un reintento de más.
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

    // Un negativo solo puede venir de una edición manual, y daría reintentos infinitos.
    return attempts < 0 ? 0 : attempts;
  }

  /// <summary>
  /// Copia las cabeceras del mensaje y deja el contador en <paramref name="attempts"/>.
  /// </summary>
  /// <remarks>
  /// Copia y no muta: el diccionario de entrada es del mensaje que estamos consumiendo.
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
