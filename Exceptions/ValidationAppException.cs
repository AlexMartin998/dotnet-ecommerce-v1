using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// 422 — validación de negocio con detalle por campo, para reglas que solo se pueden
/// evaluar contra la base. Las DataAnnotations del DTO siguen dando un 400.
/// </summary>
public sealed class ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("validation_error", "One or more business validation rules failed.", HttpStatusCode.UnprocessableEntity)
{
  /// <summary>Errores por nombre de campo, con el mismo formato que <c>ProblemDetails.errors</c>.</summary>
  public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
