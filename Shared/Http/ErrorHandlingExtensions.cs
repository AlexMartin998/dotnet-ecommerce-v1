namespace ApiEcommerce.Shared.Http;


/// <summary>Registro en DI del manejo global de errores.</summary>
public static class ErrorHandlingExtensions
{
  /// <summary>
  /// Handler global de errores + ProblemDetails (RFC 7807). Equivalente a
  /// <c>@ControllerAdvice</c> de Spring.
  /// </summary>
  public static IServiceCollection AddErrorHandling(this IServiceCollection services)
  {
    services.AddProblemDetails();
    services.AddExceptionHandler<GlobalExceptionHandler>();

    return services;
  }
}
