# 04 — Manejo de errores

Equivalente a `@ControllerAdvice` + `@ExceptionHandler` de Spring: **un solo
punto** que traduce excepciones de dominio a respuestas HTTP. Ningún controller
vuelve a escribir `try/catch`.

## Principio

> El dominio lanza **intención** (`NotFoundAppException`), la infraestructura
> HTTP decide el **código** (404). El servicio no conoce `StatusCodes`.

## Jerarquía de excepciones

`Exceptions/AppException.cs` ya define la base:

```csharp
public abstract class AppException : Exception
{
  protected AppException(string code, string message, HttpStatusCode status)
      : base(message) { Code = code; Status = status; }

  public HttpStatusCode Status { get; }   // el HTTP que corresponde
  public string Code { get; }             // slug estable para el cliente
}
```

`Code` es un identificador **estable y legible por máquina** (`not_found`,
`conflict`). El front puede hacer `switch` sobre él sin parsear mensajes en
inglés. `Message` es para humanos y puede cambiar sin romper a nadie.

### Catálogo

| Excepción | `Code` | HTTP | Cuándo se usa | Estado |
| --- | --- | --- | --- | --- |
| `NotFoundAppException(entity, key)` | `not_found` | **404** | el recurso pedido no existe | ✅ existe |
| `BadOperationAppException(message)` | `bad_request` | **400** | argumentos inválidos, FK inexistente, operación imposible | ✅ existe |
| `ConflictAppException(message)` | `conflict` | **409** | choque con el estado actual: nombre duplicado, SKU repetido | ✅ existe |
| `UnauthorizedAppException(message)` | `unauthorized` | **401** | falta credencial o el token no es válido | ✅ existe |
| `ForbiddenAppException(message)` | `forbidden` | **403** | autenticado pero sin permiso sobre el recurso | ✅ existe |
| `ValidationAppException(errors)` | `validation_error` | **422** | validación de negocio con detalle por campo | ✅ en uso (`AuthService`) |
| `PreconditionFailedAppException(message)` | `precondition_failed` | **412** | `If-Match` no casa con la versión actual | ✅ en uso (`ProductRules`) |
| `IdempotencyConflictAppException(message)` | `idempotency_key_reuse` | **422** | la misma clave con un cuerpo distinto | ✅ en uso (`CommandLog`) |
| `CustomAppException(code, msg, status)` | libre | libre | escotilla de escape para casos puntuales | ✅ existe |

`UnauthorizedAppException` y `ForbiddenAppException` las lanzan hoy `AuthService` y
`RefreshTokenService`.

⚠️ **`CustomAppException` no es la escotilla perezosa que parece.** Se usa cuando el `code`
es parte del contrato y ninguna clase existente lo aporta: `OrderService` distingue
`receipt_not_ready` (vuelve en un momento) de `receipt_failed` (ya no va a existir), los dos
409. Un solo código habría dejado al cliente haciendo polling eterno.

Reglas del catálogo:

- Toda excepción nueva **hereda de `AppException`** y fija su `Code` y `Status`
  en el constructor. Nunca se lanza `AppException` directamente (es `abstract`).
- Prefiere las tipadas sobre `CustomAppException`. Si un caso se repite tres
  veces con `CustomAppException`, merece su propia clase.
- 400 vs 409: si el request es **inválido en sí mismo**, 400; si el request es
  válido pero **choca con el estado de la base**, 409.
- 401 vs 403: 401 = "no sé quién eres"; 403 = "sé quién eres y no puedes".

## El handler global

En .NET 9 el mecanismo idiomático es `IExceptionHandler` (no un middleware
escrito a mano). Está implementado en `Shared/Http/GlobalExceptionHandler.cs`:

```csharp
using System.Net;
using ApiEcommerce.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Shared.Http;

public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService)
  : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext, Exception exception, CancellationToken ct)
  {
    var (status, code, title) = Map(exception);

    if ((int)status >= 500)
      logger.LogError(exception, "Unhandled exception on {Method} {Path}",
          httpContext.Request.Method, httpContext.Request.Path);
    else
      logger.LogWarning("{Code} on {Method} {Path}: {Message}",
          code, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

    httpContext.Response.StatusCode = (int)status;

    return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
    {
      HttpContext = httpContext,
      Exception = exception,
      ProblemDetails = new ProblemDetails
      {
        Status = (int)status,
        Title = title,
        // no se filtra el mensaje real de una excepción no controlada
        Detail = (int)status >= 500 ? "An unexpected error occurred." : exception.Message,
        Type = $"https://httpstatuses.io/{(int)status}",
        Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
        Extensions = { ["code"] = code, ["traceId"] = httpContext.TraceIdentifier }
      }
    });
  }

  private static (HttpStatusCode Status, string Code, string Title) Map(Exception ex) => ex switch
  {
    // dominio: la propia excepción trae su código
    AppException app       => (app.Status, app.Code, app.Code.Replace('_', ' ')),

    // BCL: red de seguridad mientras quede código viejo sin migrar
    KeyNotFoundException   => (HttpStatusCode.NotFound, "not_found", "Not found"),
    InvalidOperationException => (HttpStatusCode.Conflict, "conflict", "Conflict"),
    ArgumentException      => (HttpStatusCode.BadRequest, "bad_request", "Bad request"),
    UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "unauthorized", "Unauthorized"),
    OperationCanceledException  => ((HttpStatusCode)499, "client_closed_request", "Client closed request"),

    _ => (HttpStatusCode.InternalServerError, "internal_error", "Internal server error")
  };
}
```

Registro — en `ServiceCollectionExtensions.AddErrorHandling()`:

```csharp
services.AddProblemDetails();                       // formato RFC 7807
services.AddExceptionHandler<GlobalExceptionHandler>();
```

y en `Program.cs`, **lo más arriba posible del pipeline**:

```csharp
app.UseExceptionHandler();     // ANTES de UseHttpsRedirection / UseAuthorization / MapControllers
```

`UseExceptionHandler()` va lo más arriba posible del pipeline: solo captura lo
que ocurre **después** de él.

## Formato de respuesta de error

Un único formato para toda la API, basado en `ProblemDetails` (RFC 7807):

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "conflict",
  "status": 409,
  "detail": "Category 'Bebidas' already exists.",
  "instance": "POST /api/category",
  "code": "conflict",
  "traceId": "0HN7GK2P9FQ1T:00000003"
}
```

Que sea el mismo formato que produce `ValidationProblem(ModelState)` es
justamente la razón de elegir `ProblemDetails`: los errores de validación de
DataAnnotations y los de dominio se ven iguales desde el cliente.

Los errores de validación agregan `errors` con el detalle por campo:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "Name": ["Name can't be shorter than 3 characters"] }
}
```

## Reglas

1. **Ningún `try/catch` de negocio en controllers.** Ya no queda ninguno:
   `CategoryController` y `ProductController` solo validan `ModelState`, delegan y
   devuelven el camino feliz.
2. **Los servicios lanzan `AppException`.** Concretamente las clases `XRules`
   (`CategoryRules`, `ProductRules`) y `CrudService.GetOrThrowAsync`. Las ramas de
   `KeyNotFoundException` / `InvalidOperationException` del `switch` son solo una
   red de seguridad, no una alternativa válida en código nuevo.
3. **`try/catch` sí es válido** para envolver un fallo de infraestructura y
   convertirlo en excepción de dominio con contexto:
   ```csharp
   catch (DbUpdateException ex) when (IsUniqueViolation(ex))
   {
     throw new ConflictAppException($"SKU '{dto.SKU}' is already registered.");
   }
   ```
4. **Nunca se filtra el detalle de un 500** al cliente. El mensaje real va al log
   con el `traceId`; el cliente recibe un texto genérico y ese mismo `traceId`.
5. **La validación de forma es del controller** (`ModelState` + DataAnnotations,
   400); la validación de **reglas** es del servicio (excepciones de dominio).
   No mover validaciones de formato al servicio ni reglas de negocio al DTO.
6. **`ProducesResponseType` debe reflejar la realidad.** Si el servicio puede
   lanzar `ConflictAppException`, el endpoint declara `Status409Conflict`. El
   contrato de Swagger es parte de la API.
7. **404 vs lista vacía.** `GET /api/product` sin resultados devuelve `200` con
   `[]`, no 404. El 404 es solo para un recurso concreto pedido por id.

## Dónde nace cada excepción hoy

| Excepción | Origen | Endpoint que la produce |
| --- | --- | --- |
| `NotFoundAppException` | `CrudService.GetOrThrowAsync` | cualquier `GET/PATCH/DELETE /api/x/{id}` con id inexistente |
| `NotFoundAppException` | `ProductService` | `GET /api/product/category/{id}` y `POST /api/product/buy` con SKU inexistente |
| `ConflictAppException` | `CategoryRules` | `POST`/`PATCH /api/category` con nombre duplicado; `DELETE` con productos asociados |
| `ConflictAppException` | `ProductRules` | `POST`/`PATCH /api/product` con SKU duplicado |
| `ConflictAppException` | `ProductService.BuyAsync` | `POST /api/product/buy` con stock insuficiente |
| `BadOperationAppException` | `ProductRules` | `POST`/`PATCH /api/product` con `CategoryId` inexistente |

Si agregas un origen nuevo, añádelo aquí y comprueba que el
`[ProducesResponseType]` del endpoint lo declara (regla 6).
