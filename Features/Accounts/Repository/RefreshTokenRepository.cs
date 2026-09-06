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
    // Fuera del árbol de expresión: dentro, `DateTime.Now` se traduciría a `GETDATE()`, el
    // reloj del servidor SQL, y el resto del proyecto estampa con el del proceso.
    var now = DateTime.Now;

    // `RevokedAt == null` va dentro del UPDATE: dos peticiones simultáneas no lo gastan las dos.
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
      // En tandas: un DELETE de toda la tabla escala el bloqueo y corta las autenticaciones.
      => db.RefreshTokens
          .Where(t => t.ExpiresAt < cutoff)
          .OrderBy(t => t.Id)
          .Take(batchSize)
          .ExecuteDeleteAsync(ct);
}
