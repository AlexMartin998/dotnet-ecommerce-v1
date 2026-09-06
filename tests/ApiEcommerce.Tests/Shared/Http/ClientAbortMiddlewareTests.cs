using ApiEcommerce.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiEcommerce.Tests.Shared.Http;


/// <summary>Absorber las excepciones que solo ocurren porque el cliente colgó.</summary>
/// <remarks>
/// El caso real no es una <c>OperationCanceledException</c>: al cortar el cliente,
/// SqlClient lanza un <c>SqlException</c> cualquiera. Los tests usan una excepción
/// arbitraria porque la decisión se toma por el estado de la petición, no por su tipo.
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
    // 499 no es del RFC pero es la convención de nginx, y evita contarlas como 5xx.
    var context = await RunAsync(new InvalidOperationException("cancelado a media consulta"), clientAborted: true);

    Assert.Equal(499, context.Response.StatusCode);
  }

  [Fact]
  public async Task WithTheClientStillThereTheExceptionKeepsGoingUp()
  {
    // Impide que esto se coma errores de verdad: con el cliente conectado, un fallo sube.
    var boom = await Assert.ThrowsAsync<InvalidOperationException>(
        () => RunAsync(new InvalidOperationException("fallo real"), clientAborted: false));

    Assert.Equal("fallo real", boom.Message);
  }

  [Fact]
  public async Task IfTheResponseAlreadyStartedTheStatusIsNotTouched()
  {
    // Tocar el estado con las cabeceras ya enviadas lanza otra excepción encima y pierde
    // el fallo original. Hay que sustituir la feature de respuesta: la de
    // `DefaultHttpContext` devuelve `HasStarted` false siempre y el test no probaría nada.
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
