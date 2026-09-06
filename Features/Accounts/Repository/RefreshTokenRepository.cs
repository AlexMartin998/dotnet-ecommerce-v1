using ApiEcommerce.Data;
using ApiEcommerce.Features.Accounts.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Features.Accounts.Repository;


/// <inheritdoc cref="IRefreshTokenRepository"/>
public sealed class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
  public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default)
      => db.RefreshTokens.AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

  public async Task<bool> TryConsumeAsync(int id, CancellationToken ct = default)
  {
    // El instante se captura FUERA del árbol de expresión: dentro, `DateTime.Now` se
    // traduce a `GETDATE()`, o sea el reloj del SERVIDOR SQL, y el resto del proyecto
    // estampa con el del proceso.
    var now = DateTime.Now;

    // La condición `RevokedAt == null` va DENTRO del UPDATE: es lo que hace que dos
    // peticiones simultáneas con el mismo token no puedan gastarlo las dos.
    var affected = await db.RefreshTokens
        .Where(t => t.Id == id && t.RevokedAt == null)
        .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);

    return affected == 1;
  }

  public void Add(RefreshToken token) => db.RefreshTokens.Add(token);

  public Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct = default)
  {
    var now = DateTime.Now;

    return db.RefreshTokens
        .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
        .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);
  }

  public Task<int> RevokeAllForUserAsync(string userId, CancellationToken ct = default)
  {
    var now = DateTime.Now;

    return db.RefreshTokens
        .Where(t => t.UserId == userId && t.RevokedAt == null)
        .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, now), ct);
  }

  public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

  public Task<int> DeleteExpiredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken ct = default)
      // En tandas acotadas, como el resto de purgas: un DELETE de toda la tabla escala el
      // bloqueo y se lleva por delante a quien esté autenticándose.
      => db.RefreshTokens
          .Where(t => t.ExpiresAt < cutoff)
          .OrderBy(t => t.Id)
          .Take(batchSize)
          .ExecuteDeleteAsync(ct);
}
