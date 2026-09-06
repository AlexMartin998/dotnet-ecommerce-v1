namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Absorbe las excepciones que solo ocurren porque <b>el cliente colgó</b>.
/// </summary>
/// <remarks>
/// <para>
/// Cuando un cliente corta la conexión a mitad, ASP.NET Core cancela
/// <c>RequestAborted</c>; EF cancela el <c>SqlCommand</c> en vuelo y SqlClient lanza un
/// <b><c>SqlException</c></b> («A severe error occurred on the current command» /
/// «Operation cancelled by user», con un Win32 258 dentro). Eso <b>no</b> es una
/// <c>OperationCanceledException</c>, así que llegaba al final del pipeline como una
/// excepción cualquiera: <b>500, log a nivel Error y traza completa</b>.
/// </para>
/// <para>
/// Medido en las pruebas de carga: 95 «errores» que no eran errores. El coste no es la
/// respuesta —no hay nadie al otro lado para recibirla— sino el <b>ruido</b>: las métricas
/// de error y el log se llenan de incidentes falsos justo cuando más falta hace leerlos,
/// y un fallo real queda sepultado.
/// </para>
/// <para>
/// ⚠️ <b>Va por DENTRO de <c>UseExceptionHandler</c>, y ahí está el detalle que importa.</b>
/// El middleware de diagnóstico del framework escribe su «An unhandled exception has
/// occurred» a nivel Error <i>antes</i> de llamar a ningún <c>IExceptionHandler</c>, así
/// que decidirlo desde <c>GlobalExceptionHandler</c> llega tarde: la línea de Error ya está
/// escrita. Hay que interceptar antes de que la excepción llegue hasta él.
/// </para>
/// <para>
/// Se decide por el <b>estado de la petición</b> y no por el tipo de la excepción: la
/// cancelación se propaga de forma distinta según dónde pille (EF, el cliente AMQP,
/// <c>HttpClient</c>, el propio Kestrel leyendo el cuerpo), y perseguir cada tipo es una
/// lista que nunca está completa.
/// </para>
/// </remarks>
public sealed class ClientAbortMiddleware(RequestDelegate next, ILogger<ClientAbortMiddleware> logger)
{
  public async Task InvokeAsync(HttpContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    try
    {
      await next(context);
    }
    catch (Exception ex) when (context.RequestAborted.IsCancellationRequested)
    {
      // Information y SIN la excepción: es tráfico normal de una red real, no un
      // incidente. Se deja el tipo en el mensaje porque saber *por dónde* pilló la
      // cancelación sigue siendo útil para diagnosticar, y cuesta una palabra.
      logger.LogInformation(
          "Client closed the request on {Method} {Path} ({Exception})",
          context.Request.Method, context.Request.Path, ex.GetType().Name);

      // 499 (Client Closed Request): no es estándar del RFC pero es la convención de
      // facto —la usa nginx— y es lo que hace que estas peticiones no se cuenten como
      // 5xx en las métricas. Solo se pone si aún no se ha empezado a responder; si ya
      // hay cabeceras enviadas, tocarlas lanzaría otra excepción encima.
      if (!context.Response.HasStarted)
        context.Response.StatusCode = 499;

      // No se escribe cuerpo: no hay nadie al otro lado y el socket está cerrado.
    }
  }
}
