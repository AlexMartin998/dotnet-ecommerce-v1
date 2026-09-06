using ApiEcommerce.Features.Accounts.Dtos;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>
/// Sesiones: emitirlas, rotarlas y cortarlas.
/// </summary>
/// <remarks>
/// <para>
/// El access token es corto y no se puede revocar por sí solo; lo que se revoca es la
/// <b>sesión</b>, y eso vive aquí. La rotación —cada uso gasta el token y entrega otro—
/// no está para molestar: es lo que convierte un robo en algo <b>detectable</b>, porque
/// el ladrón y el dueño acaban usando el mismo token gastado.
/// </para>
/// </remarks>
public interface IRefreshTokenService
{
  /// <summary>Abre una sesión nueva y devuelve su primer refresh token, en claro.</summary>
  /// <remarks>
  /// El valor en claro se devuelve <b>una sola vez</b>, para que quien llama lo ponga en
  /// la cookie: en la base solo queda su huella y no hay forma de recuperarlo.
  /// </remarks>
  /// <param name="userId">Dueño de la sesión.</param>
  /// <param name="clientIp">Solo para investigar incidentes; no se usa para decidir nada.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<IssuedRefreshToken> IssueAsync(string userId, string? clientIp, CancellationToken ct = default);

  /// <summary>
  /// Gasta un refresh token y entrega uno nuevo, junto con un access token fresco.
  /// </summary>
  /// <remarks>
  /// Revocar el viejo y emitir el nuevo van en la <b>misma transacción</b>: si se
  /// confirmara solo lo primero, el usuario se quedaría sin sesión por un fallo nuestro.
  /// </remarks>
  /// <param name="refreshToken">El token en claro que trae el cliente.</param>
  /// <param name="clientIp">IP de origen.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <exception cref="Exceptions.UnauthorizedAppException">
  /// No existe, ya está gastado o ha caducado. <b>Si además es un reuso fuera de la
  /// ventana de gracia, la familia entera queda revocada antes de lanzar.</b>
  /// </exception>
  Task<(AuthResponseDto Auth, IssuedRefreshToken Refresh)> RotateAsync(
      string refreshToken, string? clientIp, CancellationToken ct = default);

  /// <summary>
  /// Cierra la sesión: revoca su familia y, si se puede, invalida ya el access token.
  /// </summary>
  /// <remarks>
  /// <b>No lanza si el token no existe o ya estaba revocado.</b> Cerrar sesión dos veces,
  /// o con una cookie caducada, tiene que ser inofensivo: un logout que devuelve error
  /// deja al usuario sin saber si está dentro o fuera.
  /// </remarks>
  /// <param name="refreshToken">El token en claro, o <c>null</c> si no vino cookie.</param>
  /// <param name="accessTokenId">El <c>jti</c> del access token actual, si lo hay.</param>
  /// <param name="accessTokenExpiresAt">Cuándo expira ese access token (UTC).</param>
  /// <param name="ct">Token de cancelación.</param>
  Task LogoutAsync(
      string? refreshToken, string? accessTokenId, DateTime? accessTokenExpiresAt,
      CancellationToken ct = default);
}


/// <summary>Un refresh token recién emitido, en claro y con su caducidad.</summary>
/// <param name="Token">El valor que hay que meter en la cookie. No se guarda en ningún sitio.</param>
/// <param name="ExpiresAt">Cuándo deja de valer.</param>
public readonly record struct IssuedRefreshToken(string Token, DateTime ExpiresAt);
