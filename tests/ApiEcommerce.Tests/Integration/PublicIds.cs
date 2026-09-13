using System.Text.Json;
using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Del identificador público que devuelve la API a la clave interna, y vuelta.</summary>
/// <remarks>
/// La API ya no expone la clave primaria de órdenes y pagos, pero los tests que disparan
/// el efecto a mano (eventos, repositorio) siguen hablando en ella.
/// </remarks>
internal static class PublicIds
{
  public static Guid PublicId(this JsonElement dto) => dto.GetProperty("publicId").GetGuid();

  public static async Task<int> OrderIdAsync(this ApiFactory factory, Guid publicId)
  {
    using var scope = factory.Services.CreateScope();

    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Orders.AsNoTracking()
        .Where(o => o.PublicId == publicId)
        .Select(o => o.Id)
        .SingleAsync();
  }

  public static async Task<Guid> OrderPublicIdAsync(this ApiFactory factory, int orderId)
  {
    using var scope = factory.Services.CreateScope();

    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Orders.AsNoTracking()
        .Where(o => o.Id == orderId)
        .Select(o => o.PublicId)
        .SingleAsync();
  }
}
