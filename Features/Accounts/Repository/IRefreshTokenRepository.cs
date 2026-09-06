using ApiEcommerce.Features.Accounts.Models;

namespace ApiEcommerce.Features.Accounts.Repository;


/// <summary>Acceso a los refresh tokens emitidos.</summary>
/// <remarks>
/// No hereda de <c>IBaseRepository&lt;T&gt;</c>: un refresh token no se actualiza ni se borra,
/// se gasta atómicamente, así que el CRUD solo traería métodos que nadie debe llamar.
/// </remarks>
public interface IRefreshTokenRepository
{
  /// <summary>Busca por la huella del token.</summary>
  Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

  /// <summary>Marca el token como gastado si nadie lo ha gastado ya. Atómica.</summary>
  /// <remarks>
  /// Un <c>UPDATE … WHERE RevokedAt IS NULL</c> en una sola sentencia: leer y luego escribir
  /// dejaría hueco para que dos peticiones rotaran el mismo token. <c>false</c> = llegaste
  /// tarde, y es la señal que dispara la detección de reuso.
  /// </remarks>
  Task<bool> TryConsumeAsync(int id, CancellationToken ct = default);

  /// <summary>Añade un token nuevo. <b>No hace <c>SaveChanges</c></b>.</summary>
  /// <remarks>
  /// Confirma la transacción de negocio, para que revocar el viejo y emitir el nuevo sean uno.
  /// </remarks>
  void Add(RefreshToken token);

  /// <summary>Revoca de golpe todos los tokens vivos de una familia.</summary>
  /// <returns>Cuántos se revocaron.</returns>
  Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);

  /// <summary>Revoca TODAS las sesiones vivas de un usuario, sean de la familia que sean.</summary>
  /// <remarks>
  /// Es lo que hace que bloquear una cuenta signifique algo: sin ello el usuario sigue renovando.
  /// </remarks>
  /// <returns>Cuántos se revocaron.</returns>
  Task<int> RevokeAllForUserAsync(string userId, CancellationToken ct = default);

  /// <summary>Confirma lo pendiente.</summary>
  Task SaveChangesAsync(CancellationToken ct = default);

  /// <summary>Borra los caducados hace más de <paramref name="cutoff"/>.</summary>
  Task<int> DeleteExpiredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken ct = default);
}
