using ApiEcommerce.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiEcommerce.Tests.Shared.Http;


/// <summary>
/// Absorber las excepciones que solo ocurren porque el cliente colgó.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ El caso real no es una <c>OperationCanceledException</c>: cuando el cliente corta,
/// EF cancela el <c>SqlCommand</c> y SqlClient lanza un <b><c>SqlException</c></b>, que
/// llegaba al final del pipeline como una excepción cualquiera —500, nivel Error y traza
/// completa—. Medido en las pruebas de carga: 27 «errores» que no eran errores.
/// </para>
/// <para>
/// Aquí se usa una excepción cualquiera a propósito: lo que se está fijando es que la
/// decisión se toma por el <b>estado de la petición</b> y no por el tipo de la excepción.
/// <c>SqlException</c> no se puede construir en un test —no tiene constructor público—,
/// que es justo por lo que perseguir tipos concretos era mal diseño.
/// </para>
/// </remarks>
public class ClientAbortMiddlewareTests
{
  private static async Task<HttpContext> RunAsync(Exception thrown, bool clientAborted)
  {
    var context = new DefaultHttpContext();
    context.Request.Method = "POST";
    context.Request.Path = "/api/v1/product/buy";

    if (clientAborted)
    {
      using var aborted = new CancellationTokenSource();
      await aborted.CancelAsync();
      context.RequestAborted = aborted.Token;
    }

    var sut = new ClientAbortMiddleware(
        _ => throw thrown, NullLogger<ClientAbortMiddleware>.Instance);

    await sut.InvokeAsync(context);

    return context;
  }

  [Fact]
  public async Task WhenTheClientHungUpTheRequestIsSwallowedAndMarked499()
  {
    // 499 (Client Closed Request) no es del RFC pero es la convención de facto —la de
    // nginx— y es lo que hace que estas peticiones no cuenten como 5xx en las métricas.
    var context = await RunAsync(new InvalidOperationException("cancelado a media consulta"), clientAborted: true);

    Assert.Equal(499, context.Response.StatusCode);
  }

  [Fact]
  public async Task WithTheClientStillThereTheExceptionKeepsGoingUp()
  {
    // La otra mitad, y la que impide que esto se coma errores de verdad: si el cliente
    // sigue conectado, un fallo es un fallo y tiene que llegar al handler global.
    var boom = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RunAsync(new InvalidOperationException("fallo real"), clientAborted: false));

    Assert.Equal("fallo real", boom.Message);
  }

  [Fact]
  public async Task IfTheResponseAlreadyStartedTheStatusIsNotTouched()
  {
    // Tocar el código de estado con las cabeceras ya enviadas lanza otra excepción
    // encima de la primera, y entonces sí se pierde la información del fallo original.
    //
    // ⚠️ Hace falta sustituir la característica de respuesta: la de `DefaultHttpContext`
    // devuelve `HasStarted` false SIEMPRE, así que un test escrito con ella pasa sin
    // probar nada — y este guard es justo el que evita convertir un fallo en dos.
    var context = new DefaultHttpContext();
    context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());

    using var aborted = new CancellationTokenSource();
    await aborted.CancelAsync();
    context.RequestAborted = aborted.Token;

    var sut = new ClientAbortMiddleware(
        _ => throw new InvalidOperationException("tarde"),
        NullLogger<ClientAbortMiddleware>.Instance);

    await sut.InvokeAsync(context);

    Assert.True(context.Response.HasStarted);
    Assert.Equal(200, context.Response.StatusCode);   // el que ya se había enviado
  }

  /// <summary>Respuesta que ya empezó a enviarse.</summary>
  private sealed class StartedResponseFeature : IHttpResponseFeature
  {
    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
    public Stream Body { get; set; } = Stream.Null;
    public bool HasStarted => true;

    public void OnStarting(Func<object, Task> callback, object state) { }
    public void OnCompleted(Func<object, Task> callback, object state) { }
  }
}
