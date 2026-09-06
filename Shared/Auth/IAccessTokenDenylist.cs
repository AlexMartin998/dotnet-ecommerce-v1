namespace ApiEcommerce.Shared.Auth;


/// <summary>
/// Access tokens que ya no valen aunque su firma siga siendo buena y no hayan expirado.
/// </summary>
/// <remarks>
/// Es una optimización, no la garantía: lo que corta una sesión es revocar su familia de
/// refresh tokens en la base. Por eso puede vivir en Redis y degradar en abierto, y por
/// eso el TTL de cada entrada es lo que le quede de vida al token, ni un segundo más.
/// </remarks>
public interface IAccessTokenDenylist
{
  /// <summary>Invalida un access token por su <c>jti</c> hasta que expire.</summary>
  /// <param name="tokenId">El claim <c>jti</c>.</param>
  /// <param name="expiresAt">Cuándo expira el token (UTC).</param>
  /// <param name="ct">Token de cancelación.</param>
  Task RevokeAsync(string tokenId, DateTime expiresAt, CancellationToken ct = default);

  /// <summary>¿Está revocado?</summary>
  /// <remarks>
  /// Corre en cada petición autenticada, así que ante la duda deja pasar: lo contrario
  /// convertiría un corte de Redis en «nadie puede usar la API».
  /// </remarks>
  /// <param name="tokenId">El claim <c>jti</c>.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<bool> IsRevokedAsync(string tokenId, CancellationToken ct = default);
}
