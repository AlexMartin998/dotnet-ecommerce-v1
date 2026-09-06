using ApiEcommerce.Features.Accounts.Dtos;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>Sesiones: emitirlas, rotarlas y cortarlas.</summary>
/// <remarks>
/// Lo revocable es la sesión, no el access token. La rotación —cada uso gasta el token y
/// entrega otro— hace detectable un robo: ladrón y dueño acaban usando el mismo token gastado.
/// </remarks>
public interface IRefreshTokenService
{
  /// <summary>Abre una sesión nueva y devuelve su primer refresh token, en claro.</summary>
  /// <remarks>
  /// El valor en claro se devuelve una sola vez: en la base solo queda su huella.
  /// </remarks>
  /// <param name="userId">Dueño de la sesión.</param>
  /// <param name="clientIp">Solo para investigar incidentes; no se usa para decidir nada.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<IssuedRefreshToken> IssueAsync(string userId, string? clientIp, CancellationToken ct = default);

  /// <summary>Gasta un refresh token y entrega uno nuevo, con un access token fresco.</summary>
  /// <remarks>
  /// Revocar el viejo y emitir el nuevo van en la misma transacción: confirmar solo lo
  /// primero dejaría al usuario sin sesión.
  /// </remarks>
  /// <param name="refreshToken">El token en claro que trae el cliente.</param>
  /// <param name="clientIp">IP de origen.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <exception cref="Exceptions.UnauthorizedAppException">
  /// No existe, ya está gastado o ha caducado. Si es un reuso fuera de la ventana de gracia,
  /// la familia entera queda revocada antes de lanzar.
  /// </exception>
  Task<(AuthResponseDto Auth, IssuedRefreshToken Refresh)> RotateAsync(
      string refreshToken, string? clientIp, CancellationToken ct = default);

  /// <summary>Cierra la sesión: revoca su familia y, si puede, invalida ya el access token.</summary>
  /// <remarks>
  /// No lanza si el token no existe o ya estaba revocado: cerrar sesión dos veces tiene que
  /// ser inofensivo.
  /// </remarks>
  /// <param name="refreshToken">El token en claro, o <c>null</c> si no vino cookie.</param>
  /// <param name="accessTokenId">El <c>jti</c> del access token actual, si lo hay.</param>
  /// <param name="accessTokenExpiresAt">Cuándo expira ese access token (UTC).</param>
  /// <param name="ct">Token de cancelación.</param>
  Task LogoutAsync(
      string? refreshToken, string? accessTokenId, DateTime? accessTokenExpiresAt,
      CancellationToken ct = default);

  /// <summary>Corta todas las sesiones de un usuario, esté donde esté conectado.</summary>
  /// <remarks>
  /// Los access tokens ya emitidos no se invalidan aquí —la denylist va por <c>jti</c> y no se
  /// conocen— y sobreviven lo que les quede de vida, pero en ese rato ya no se puede renovar.
  /// </remarks>
  /// <param name="userId">De quién.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task RevokeAllSessionsAsync(string userId, CancellationToken ct = default);
}


/// <summary>Un refresh token recién emitido, en claro y con su caducidad.</summary>
/// <param name="Token">El valor que va en la cookie. No se guarda en claro en ningún sitio.</param>
/// <param name="ExpiresAt">Cuándo deja de valer.</param>
public readonly record struct IssuedRefreshToken(string Token, DateTime ExpiresAt);
