namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Absorbe las excepciones que solo ocurren porque el cliente colgó, para que no se
/// registren como incidentes.
/// </summary>
/// <remarks>
/// Va por dentro de <c>UseExceptionHandler</c> porque el middleware del framework escribe
/// su línea de Error antes de llamar a ningún <c>IExceptionHandler</c>. Se decide por el
/// estado de la petición y no por el tipo de excepción, que varía según dónde pille.
/// </remarks>
public sealed class ClientAbortMiddleware(RequestDelegate next, ILogger<ClientAbortMiddleware> logger)
{
  /// <summary>Ejecuta el resto del pipeline y traduce la cancelación del cliente a un 499.</summary>
  public async Task InvokeAsync(HttpContext context)
  {
    ArgumentNullException.ThrowIfNull(context);

    try
    {
      await next(context);
    }
    catch (Exception ex) when (context.RequestAborted.IsCancellationRequested)
    {
      // Information y sin la excepción: es tráfico normal de una red real. El tipo se deja
      // en el mensaje porque saber por dónde pilló la cancelación sigue siendo útil.
      logger.LogInformation(
          "Client closed the request on {Method} {Path} ({Exception})",
          context.Request.Method, context.Request.Path, ex.GetType().Name);

      // 499 (Client Closed Request) es la convención de facto de nginx, y evita que estas
      // peticiones cuenten como 5xx. Solo si aún no se han enviado cabeceras.
      if (!context.Response.HasStarted)
        context.Response.StatusCode = 499;

      // No se escribe cuerpo: no hay nadie al otro lado y el socket está cerrado.
    }
  }
}
