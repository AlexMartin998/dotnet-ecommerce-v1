using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace ApiEcommerce.Tests.Shared.Http;


/// <summary>
/// Traducción de excepción a <c>ProblemDetails</c>. Es el único sitio del proyecto que
/// decide códigos HTTP, así que un fallo aquí se nota en TODOS los endpoints a la vez.
/// </summary>
public class GlobalExceptionHandlerTests
{
  private readonly Mock<IProblemDetailsService> _problemDetails = new();
  private ProblemDetails? _written;

  public GlobalExceptionHandlerTests()
      => _problemDetails.Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
             .Callback<ProblemDetailsContext>(c => _written = c.ProblemDetails)
             .ReturnsAsync(true);

  private async Task<HttpContext> HandleAsync(Exception exception)
  {
    var context = new DefaultHttpContext();
    context.Request.Method = "POST";
    context.Request.Path = "/api/v1/product";

    var sut = new GlobalExceptionHandler(
        NullLogger<GlobalExceptionHandler>.Instance, _problemDetails.Object);

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
    // Por debajo de 500 el mensaje SÍ viaja: es información útil que el dominio
    // escribió a propósito para el cliente.
    Assert.Equal("Category 'Bebidas' already exists.", _written.Detail);
  }

  [Fact]
  public async Task AValidationAppException_CarriesTheErrorsPerField()
  {
    var errors = new Dictionary<string, string[]> { ["Password"] = ["demasiado corta"] };

    await HandleAsync(new ValidationAppException(errors));

    // Mismo formato `errors` que produce ValidationProblem(ModelState): el cliente
    // recibe siempre la misma forma venga de DataAnnotations o de una regla de negocio.
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
    // ⚠️ DESNUDO. Es lo que lanza ExecuteUpdateAsync, que no pasa por SaveChangesAsync:
    // justamente TryDecrementStockAsync, la sentencia con más contención del sistema.
    // La versión anterior del handler hacía pattern matching sobre
    // `DbUpdateException { InnerException: SqlException }` y este caso salía 500.
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
    // Y con EnableRetryOnFailure agotado llega envuelto DOS veces. Por eso se RECORRE
    // la cadena de InnerException en vez de mirar una forma concreta de anidamiento:
    // el anidamiento no es estable y depende de por dónde entró la excepción.
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
    // ⚠️ El test que protege una decisión CONTRAINTUITIVA, y por eso el que más falta
    // hace. EF Core usa InvalidOperationException para errores de PROGRAMACIÓN ("the
    // instance of entity type X cannot be tracked because..."). Mapearla a 409 daba el
    // código equivocado Y filtraba mensajes internos del ORM al cliente, porque Detail
    // solo se censura a partir de 500. Que caiga a 500, que es lo que realmente es.
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
    // El cliente cerró la conexión. No es culpa del servidor y no debe contar como 5xx
    // en las métricas ni disparar alertas.
    var context = await HandleAsync(new OperationCanceledException());

    Assert.Equal(499, context.Response.StatusCode);
  }

  [Fact]
  public async Task EveryProblemCarriesTheCorrelationIdAndTheInstance()
  {
    // Sin un id, un 500 en producción es un ticket sin forma de correlacionarlo con la
    // línea del log. Y tiene que ser EL MISMO que viaja en la cabecera X-Correlation-Id:
    // antes era `TraceIdentifier`, así que el cuerpo y la cabecera llevaban dos ids
    // distintos para la misma petición.
    var context = await HandleAsync(new Exception("boom"));

    Assert.Equal(context.TraceIdentifier, _written!.Extensions["correlationId"]);
    Assert.Equal("POST /api/v1/product", _written.Instance);
  }

  [Fact]
  public async Task TheProblemUsesTheCorrelationIdChosenByTheMiddleware()
  {
    // Cuando el middleware ya decidió uno (el del cliente, o el generado), el cuerpo debe
    // llevar ESE y no el identificador interno del framework.
    var context = new DefaultHttpContext();
    context.Request.Method = "POST";
    context.Request.Path = "/api/v1/product";
    context.Items[CorrelationIdMiddleware.HeaderName] = "CID-DEL-CLIENTE";

    var sut = new GlobalExceptionHandler(
        NullLogger<GlobalExceptionHandler>.Instance, _problemDetails.Object);

    await sut.TryHandleAsync(context, new Exception("boom"), CancellationToken.None);

    Assert.Equal("CID-DEL-CLIENTE", _written!.Extensions["correlationId"]);
  }
}
