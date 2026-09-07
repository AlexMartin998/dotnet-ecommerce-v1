using ApiEcommerce.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;


namespace ApiEcommerce.Shared.Db;



/// <summary>
/// Envuelve la acción en una transacción de EF Core. Para una unidad de trabajo con
/// reintento, usa <see cref="ITransactionRunner"/> desde el servicio.
/// </summary>
/// <remarks>
/// <c>ActionExecutionDelegate</c> no es reentrante, así que este filtro no puede
/// reejecutarse cuando la estrategia reintenta: una guarda convierte esa situación en un
/// error ruidoso en vez de en doble efecto o commit vacío.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TransactionalAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
  /// <summary>Por DENTRO de <c>[Idempotent]</c> (Order -100): su puerta se cruza antes de abrir la transacción.</summary>
  public int Order => 0;

  /// <summary>Abre la transacción, ejecuta la acción y confirma si no hubo excepción.</summary>
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
    var ct = context.HttpContext.RequestAborted;
    var strategy = db.Database.CreateExecutionStrategy();

    var invoked = false;

    await strategy.ExecuteAsync(async () =>
    {
      // Guarda de reentrada: `next()` ya se consumió, y volver a invocarlo reejecutaría
      // la acción. Fallar ruidosamente es mejor que descontar stock dos veces en silencio.
      if (invoked)
        throw new InvalidOperationException(
            "[Transactional] no puede reintentarse: ActionExecutionDelegate no es reentrante. " +
            "Mueve la unidad transaccional al servicio con ITransactionRunner.");

      invoked = true;

      // `await using` ya hace rollback al disponerse, y así un rollback que lance no
      // sustituye a la excepción original.
      // Con EnableRetryOnFailure, EF PROHÍBE BeginTransactionAsync fuera de strategy.ExecuteAsync.
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      var executed = await next(); // ejecuta la acción

      if (executed.Exception is null || executed.ExceptionHandled)
        await tx.CommitAsync(CancellationToken.None);
    });
  }
}
