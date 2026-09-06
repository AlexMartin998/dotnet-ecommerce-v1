using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ApiEcommerce.Tests.Shared.Http;


/// <summary>
/// Traducción de excepción a <c>ProblemDetails</c>. Es el único sitio que decide códigos
/// HTTP, así que un fallo aquí se nota en todos los endpoints a la vez.
/// </summary>
public class GlobalExceptionHandlerTests
{
  private readonly Mock<IProblemDetailsService> _problemDetails = new();
  private ProblemDetails? _written;

  public GlobalExceptionHandlerTests()
      => _problemDetails.Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
             .Callback<ProblemDetailsContext>(c => _written = c.ProblemDetails)
             .ReturnsAsync(true);

  private async Task<HttpContext> HandleAsync(
      Exception exception, ILogger<GlobalExceptionHandler>? logger = null)
  {
    var context = new DefaultHttpContext();
    context.Request.Method = "POST";
    context.Request.Path = "/api/v1/product";

    var sut = new GlobalExceptionHandler(
        logger ?? NullLogger<GlobalExceptionHandler>.Instance, _problemDetails.Object);

    Assert.True(await sut.TryHandleAsync(context, exception, CancellationToken.None));
    return context;
  }

  // ---- dominio ------------------------------------------------------------

  [Fact]
  public async Task AnAppException_BringsItsOwnStatusAndCode()
  {
    var context = await HandleAsync(new ConflictAppException("Category 'Bebidas' already exists."));

    Assert.Equal(409, context.Response.StatusCode);
    Assert.Equal("conflict", _written!.Extensions["code"]);
    // Por debajo de 500 el mensaje viaja: el dominio lo escribió para el cliente.
    Assert.Equal("Category 'Bebidas' already exists.", _written.Detail);
  }

  [Fact]
  public async Task AValidationAppException_CarriesTheErrorsPerField()
  {
    var errors = new Dictionary<string, string[]> { ["Password"] = ["demasiado corta"] };

    await HandleAsync(new ValidationAppException(errors));

    // Mismo formato `errors` que ValidationProblem(ModelState), venga de donde venga.
    Assert.True(_written!.Extensions.ContainsKey("errors"));
  }

  // ---- carreras que la validación previa no puede evitar ------------------

  [Theory]
  [InlineData(2601, 409, "conflict")]    // índice único violado
  [InlineData(2627, 409, "conflict")]    // restricción de clave única violada
  [InlineData(1205, 409, "deadlock")]
  [InlineData(547, 409, "fk_violation")]
  public async Task ABareSqlException_IsTranslated(int number, int expectedStatus, string expectedCode)
  {
    // Desnudo: es lo que lanza ExecuteUpdateAsync, que no pasa por SaveChangesAsync, y es
    // el camino de TryDecrementStockAsync, la sentencia con más contención.
    var context = await HandleAsync(SqlExceptionFactory.WithNumber(number));

    Assert.Equal(expectedStatus, context.Response.StatusCode);
    Assert.Equal(expectedCode, _written!.Extensions["code"]);
  }

  [Fact]
  public async Task ANestedSqlException_IsTranslatedToo()
  {
    // ANIDADO. Es lo que lanza SaveChangesAsync: DbUpdateException { SqlException }.
    var context = await HandleAsync(
        new DbUpdateException("error al guardar", SqlExceptionFactory.WithNumber(2601)));

    Assert.Equal(409, context.Response.StatusCode);
    Assert.Equal("conflict", _written!.Extensions["code"]);
  }

  [Fact]
  public async Task ADeeplyNestedSqlException_IsFoundByWalkingTheChain()
  {
    // Con EnableRetryOnFailure agotado llega envuelto dos veces: el anidamiento no es
    // estable, por eso se recorre la cadena en vez de mirar una forma concreta.
    var deep = new InvalidOperationException("reintentos agotados",
        new DbUpdateException("error al guardar", SqlExceptionFactory.WithNumber(2627)));

    var context = await HandleAsync(deep);

    Assert.Equal(409, context.Response.StatusCode);
  }

  [Fact]
  public async Task AConcurrencyConflict_Is409AndSaysItCanBeRetried()
  {
    var context = await HandleAsync(new DbUpdateConcurrencyException("otro request la modificó"));

    Assert.Equal(409, context.Response.StatusCode);
    Assert.Equal("concurrency_conflict", _written!.Extensions["code"]);
  }

  // ---- red de seguridad y lo que NO se mapea ------------------------------

  [Theory]
  [InlineData(typeof(KeyNotFoundException), 404)]
  [InlineData(typeof(ArgumentException), 400)]
  [InlineData(typeof(UnauthorizedAccessException), 401)]
  public async Task TheBclSafetyNetIsNarrow(Type exceptionType, int expectedStatus)
  {
    var context = await HandleAsync((Exception)Activator.CreateInstance(exceptionType)!);

    Assert.Equal(expectedStatus, context.Response.StatusCode);
  }

  [Fact]
  public async Task AnInvalidOperationExceptionIsDeliberatelyNotMapped()
  {
    // EF Core usa InvalidOperationException para errores de programación: mapearla a 409
    // daría el código equivocado y filtraría mensajes del ORM, que solo se censuran en 500.
    var context = await HandleAsync(new InvalidOperationException("detalle interno de EF Core"));

    Assert.Equal(500, context.Response.StatusCode);
    Assert.Equal("An unexpected error occurred.", _written!.Detail);
    Assert.DoesNotContain("EF Core", _written.Detail);
  }

  [Fact]
  public async Task AnUnknownException_Is500AndNeverLeaksItsMessage()
  {
    var context = await HandleAsync(new Exception("cadena de conexión: Password=SuperSecreto"));

    Assert.Equal(500, context.Response.StatusCode);
    Assert.Equal("An unexpected error occurred.", _written!.Detail);
    Assert.DoesNotContain("SuperSecreto", _written.Detail);
  }

  [Fact]
  public async Task ACancelledRequest_Is499AndNotAnError()
  {
    // El cliente cerró la conexión: no es un 5xx ni debe disparar alertas.
    var context = await HandleAsync(new OperationCanceledException());

    Assert.Equal(499, context.Response.StatusCode);
  }

  [Fact]
  public async Task EveryProblemCarriesTheCorrelationIdAndTheInstance()
  {
    // Sin id, un 500 es un ticket que no se puede cruzar con el log. Y tiene que ser el
    // mismo que viaja en X-Correlation-Id, o cuerpo y cabecera dirían cosas distintas.
    var context = await HandleAsync(new Exception("boom"));

    Assert.Equal(context.TraceIdentifier, _written!.Extensions["correlationId"]);
    Assert.Equal("POST /api/v1/product", _written.Instance);
  }

  [Fact]
  public async Task TheProblemUsesTheCorrelationIdChosenByTheMiddleware()
  {
    // Si el middleware ya decidió uno, el cuerpo lleva ese y no el id interno del framework.
    var context = new DefaultHttpContext();
    context.Request.Method = "POST";
    context.Request.Path = "/api/v1/product";
    context.Items[CorrelationIdMiddleware.HeaderName] = "CID-DEL-CLIENTE";

    var sut = new GlobalExceptionHandler(
        NullLogger<GlobalExceptionHandler>.Instance, _problemDetails.Object);

    await sut.TryHandleAsync(context, new Exception("boom"), CancellationToken.None);

    Assert.Equal("CID-DEL-CLIENTE", _written!.Extensions["correlationId"]);
  }

  // ---- registro en el log --------------------------------------------------
  //
  // El logger del ExceptionHandlerMiddleware está silenciado en appsettings, así que este
  // handler es el único que registra excepciones: solo estos dos tests lo protegen.

  [Fact]
  public async Task AnUnmappedException_IsLoggedAsAnErrorWithItsStackTrace()
  {
    var logger = new Mock<ILogger<GlobalExceptionHandler>>();

    await HandleAsync(new InvalidOperationException("algo se rompió de verdad"), logger.Object);

    // La excepción va como excepción y no interpolada: es lo que lleva la traza al log.
    logger.Verify(l => l.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.Is<Exception>(e => e is InvalidOperationException),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
  }

  [Fact]
  public async Task ADomainException_IsAWarningAndNotAnError()
  {
    // Quedarse sin stock es el resultado normal de una compra concurrente: como Error
    // llenaría de incidentes falsos el log que hay que leer cuando pasa algo de verdad.
    var logger = new Mock<ILogger<GlobalExceptionHandler>>();

    await HandleAsync(new ConflictAppException("Insufficient stock for SKU 'X'."), logger.Object);

    logger.Verify(l => l.Log(
        LogLevel.Error,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

    logger.Verify(l => l.Log(
        LogLevel.Warning,
        It.IsAny<EventId>(),
        It.IsAny<It.IsAnyType>(),
        It.IsAny<Exception>(),
        It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
  }
}
