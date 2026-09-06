using ApiEcommerce.Features.Accounts.Models;

namespace ApiEcommerce.Features.Accounts.Repository;


/// <summary>
/// Acceso a los refresh tokens emitidos.
/// </summary>
/// <remarks>
/// No hereda de <c>IBaseRepository&lt;T&gt;</c> a propósito: de las cinco operaciones CRUD
/// aquí no se usa ninguna tal cual. Un refresh token no se "actualiza" ni se "borra": se
/// <b>gasta</b>, y esa operación tiene que ser atómica. Heredar el CRUD solo traería cinco
/// métodos que nadie debe llamar.
/// </remarks>
public interface IRefreshTokenRepository
{
  /// <summary>Busca por la huella del token.</summary>
  Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken ct = default);

  /// <summary>
  /// Marca el token como gastado <b>si nadie lo ha gastado ya</b>. Operación atómica.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ Es un <c>UPDATE … WHERE RevokedAt IS NULL</c> en <b>una sola sentencia</b>, no un
  /// leer-y-luego-escribir. Entre comprobar «está vivo» y marcarlo cabe otra petición, y
  /// entonces las dos rotarían el mismo token y habría dos sesiones válidas donde debía
  /// haber una. Es la misma razón por la que el stock se descuenta con un UPDATE
  /// condicional y no comprobando antes.
  /// </para>
  /// <para>
  /// Devolver <c>false</c> significa «llegaste tarde», y es justo la señal que dispara la
  /// detección de reuso.
  /// </para>
  /// </remarks>
  Task<bool> TryConsumeAsync(int id, CancellationToken ct = default);

  /// <summary>Añade un token nuevo. <b>No hace <c>SaveChanges</c></b>.</summary>
  /// <remarks>
  /// Como <c>IEventOutbox</c>: quien confirma es la transacción de negocio, para que
  /// revocar el viejo y emitir el nuevo sean una sola cosa.
  /// </remarks>
  void Add(RefreshToken token);

  /// <summary>Revoca de golpe todos los tokens vivos de una familia.</summary>
  /// <returns>Cuántos se revocaron.</returns>
  Task<int> RevokeFamilyAsync(Guid familyId, CancellationToken ct = default);

  /// <summary>Revoca TODAS las sesiones vivas de un usuario, sean de la familia que sean.</summary>
  /// <remarks>
  /// Es lo que hace que bloquear una cuenta signifique algo: sin esto el usuario sigue
  /// dentro y puede seguir renovando indefinidamente.
  /// </remarks>
  /// <returns>Cuántos se revocaron.</returns>
  Task<int> RevokeAllForUserAsync(string userId, CancellationToken ct = default);

  /// <summary>Confirma lo pendiente.</summary>
  Task SaveChangesAsync(CancellationToken ct = default);

  /// <summary>Borra los caducados hace más de <paramref name="cutoff"/>.</summary>
  Task<int> DeleteExpiredBeforeAsync(DateTime cutoff, int batchSize, CancellationToken ct = default);
}
