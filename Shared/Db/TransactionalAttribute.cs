using ApiEcommerce.Data;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;


namespace ApiEcommerce.Shared.Db;



[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class TransactionalAttribute : Attribute, IAsyncActionFilter
{
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
    var strategy = db.Database.CreateExecutionStrategy();

    await strategy.ExecuteAsync(async () =>
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      try
      {
        var executed = await next(); // ejecuta la acción

        if (executed.Exception is null || executed.ExceptionHandled)
        {
          // OJO: asumimos que tus repos ya llamaron Save() internamente.
          await tx.CommitAsync();
        }
        else
        {
          await tx.RollbackAsync();
        }
      }
      catch
      {
        await tx.RollbackAsync();
        throw;
      }
    });
  }
}
