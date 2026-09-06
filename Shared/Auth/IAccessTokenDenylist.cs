namespace ApiEcommerce.Shared.Auth;


/// <summary>
/// Access tokens que ya no valen aunque su firma siga siendo buena y no hayan expirado.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Esto es una optimización, no la garantía.</b> Lo que de verdad corta una sesión es
/// revocar su familia de refresh tokens, que vive en la base y se confirma en una
/// transacción: sin eso, la sesión se puede seguir extendiendo indefinidamente. La
/// denylist solo adelanta el efecto al access token que el cliente <i>ya tiene en la
/// mano</i>.
/// </para>
/// <para>
/// Por eso puede vivir en Redis y degradar en abierto: si no está, un logout sigue
/// cortando la sesión y lo único que sobrevive es el access token actual, <b>como mucho
/// lo que dure</b> (15 minutos). Es exactamente la distinción que fija <c>rules.md</c> §8:
/// degrada lo que tiene plan B, no lo que es la garantía.
/// </para>
/// <para>
/// El TTL de cada entrada es lo que le quede de vida al token, ni un segundo más: pasado
/// eso, la firma ya no vale por sí sola y guardarlo sería pagar memoria por nada.
/// </para>
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
  /// Corre en <b>cada petición autenticada</b>, así que tiene que ser barato y no puede
  /// tumbar la API si el almacén no responde: ante la duda, deja pasar. Lo contrario
  /// convertiría un corte de Redis en «nadie puede usar la API».
  /// </remarks>
  /// <param name="tokenId">El claim <c>jti</c>.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<bool> IsRevokedAsync(string tokenId, CancellationToken ct = default);
}
