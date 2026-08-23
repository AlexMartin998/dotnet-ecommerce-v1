using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// 422 — validación de <b>negocio</b> con detalle por campo. No sustituye a las
/// DataAnnotations del DTO (esas producen un 400 vía <c>ValidationProblem(ModelState)</c>);
/// es para reglas que solo se pueden evaluar contra la base y que conviene devolver
/// agrupadas por campo.
/// </summary>
public sealed class ValidationAppException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("validation_error", "One or more business validation rules failed.", HttpStatusCode.UnprocessableEntity)
{
  /// <summary>Errores por nombre de campo, con el mismo formato que <c>ProblemDetails.errors</c>.</summary>
  public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
