using ApiEcommerce.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;


namespace ApiEcommerce.Shared.Db;



/// <remarks>
/// <para>
/// <b>Limitación importante.</b> Con <c>EnableRetryOnFailure</c> activo, EF obliga a
/// meter la transacción dentro de <c>strategy.ExecuteAsync</c>, y esa estrategia
/// <b>reejecuta el delegado</b> ante un fallo transitorio. Pero
/// <c>ActionExecutionDelegate</c> <b>no es reentrante</b>: invocarlo dos veces vuelve
/// a ejecutar la acción sobre el mismo <c>DbContext</c>, con doble efecto o con un
/// commit vacío según el estado interno de MVC. Por eso hay una guarda que convierte
/// esa situación en un error ruidoso en vez de en corrupción silenciosa.
/// </para>
/// <para>
/// <b>Para una unidad de trabajo transaccional de verdad, usa
/// <see cref="ITransactionRunner"/> desde el servicio</b>, que sí es replayable.
/// Este atributo se queda para acciones simples que no necesitan reintento.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TransactionalAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
  /// <summary>Por DENTRO de <c>[Idempotent]</c> (Order -100): la respuesta se memoriza tras el commit.</summary>
  public int Order => 0;

  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
    var ct = context.HttpContext.RequestAborted;
    var strategy = db.Database.CreateExecutionStrategy();

    var invoked = false;

    await strategy.ExecuteAsync(async () =>
    {
      // Guarda de reentrada: si la estrategia reintenta, `next()` ya se consumió y
      // volver a invocarlo reejecutaría la acción. Fallar aquí, ruidosamente, es
      // infinitamente mejor que descontar stock dos veces en silencio.
      if (invoked)
        throw new InvalidOperationException(
            "[Transactional] no puede reintentarse: ActionExecutionDelegate no es reentrante. " +
            "Mueve la unidad transaccional al servicio con ITransactionRunner.");

      invoked = true;

      // `await using` ya hace rollback al disponerse si no se hizo commit, así que no
      // hace falta un RollbackAsync explícito. Además evita el bug clásico de este
      // patrón: si el rollback lanza dentro de un catch, sustituye a la excepción
      // original y el cliente ve el fallo del rollback en vez de la causa real.
      await using var tx = await db.Database.BeginTransactionAsync(ct);

      var executed = await next(); // ejecuta la acción

      if (executed.Exception is null || executed.ExceptionHandled)
        await tx.CommitAsync(CancellationToken.None);
    });
  }
}
