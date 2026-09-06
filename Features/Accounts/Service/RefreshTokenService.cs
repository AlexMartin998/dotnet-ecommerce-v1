using System.Security.Cryptography;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Accounts.Repository;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Db;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Accounts.Service;


/// <inheritdoc cref="IRefreshTokenService"/>
public sealed class RefreshTokenService(
    IRefreshTokenRepository repository,
    UserManager<ApplicationUser> userManager,
    IJwtTokenService tokenService,
    IAccessTokenDenylist denylist,
    ITransactionRunner transactions,
    IOptions<RefreshTokenOptions> options,
    ILogger<RefreshTokenService> logger) : IRefreshTokenService
{
  private readonly RefreshTokenOptions _options = options.Value;

  public async Task<IssuedRefreshToken> IssueAsync(
      string userId, string? clientIp, CancellationToken ct = default)
  {
    var issued = Create(userId, Guid.NewGuid(), clientIp);

    await repository.SaveChangesAsync(ct);

    return issued;
  }

  public async Task<(AuthResponseDto Auth, IssuedRefreshToken Refresh)> RotateAsync(
      string refreshToken, string? clientIp, CancellationToken ct = default)
  {
    var stored = await repository.FindByHashAsync(HashOf(refreshToken), ct)
        ?? throw new UnauthorizedAppException("Invalid refresh token.");

    var now = DateTime.Now;

    // Detección de reuso: un token ya gastado que reaparece es robo o una carrera del cliente.
    if (stored.RevokedAt is { } revokedAt)
    {
      // Dentro de la ventana de gracia se rechaza pero NO se revoca la familia: es una carrera.
      if (now - revokedAt <= _options.ReuseGrace)
      {
        logger.LogInformation(
            "Refresh token for user {UserId} was already rotated {Ago:0.0}s ago; treating it as a client race",
            stored.UserId, (now - revokedAt).TotalSeconds);

        throw new UnauthorizedAppException("Invalid refresh token.");
      }

      // Fuera de la ventana es robo: no se sabe qué copia es la del dueño, cae la sesión entera.
      var revoked = await repository.RevokeFamilyAsync(stored.FamilyId, ct);

      logger.LogWarning(
          "Refresh token REUSE detected for user {UserId}; revoked {Count} token(s) of family {FamilyId}",
          stored.UserId, revoked, stored.FamilyId);

      throw new UnauthorizedAppException("Invalid refresh token.");
    }

    if (stored.ExpiresAt <= now)
      throw new UnauthorizedAppException("Invalid refresh token.");

    var user = await userManager.FindByIdAsync(stored.UserId)
        // La cuenta ya no existe: 401 y no 404, distinguirlo sería un oráculo.
        ?? throw new UnauthorizedAppException("Invalid refresh token.");

    // Renovar no vuelve a pedir credenciales: sin esto una cuenta bloqueada renovaría para siempre.
    if (await userManager.IsLockedOutAsync(user))
    {
      logger.LogWarning("Refresh refused for locked-out user {UserId}", stored.UserId);
      throw new UnauthorizedAppException("Invalid refresh token.");
    }

    var roles = await userManager.GetRolesAsync(user);

    // Gastar el viejo y emitir el nuevo van juntos: confirmar solo lo primero deja al usuario sin sesión.
    var issued = await transactions.ExecuteAsync(async token =>
    {
      // El UPDATE condicional arbitra: de dos peticiones con el mismo token, solo una lo gasta.
      if (!await repository.TryConsumeAsync(stored.Id, token))
        throw new UnauthorizedAppException("Invalid refresh token.");

      var next = Create(stored.UserId, stored.FamilyId, clientIp);

      await repository.SaveChangesAsync(token);

      return next;
    }, ct);

    var (accessToken, expiresAt) = tokenService.CreateToken(user, roles);

    var auth = new AuthResponseDto
    {
      Token = accessToken,
      ExpiresAt = expiresAt,
      User = new UserDto
      {
        Id = user.Id,
        Username = user.UserName ?? string.Empty,
        Email = user.Email ?? string.Empty,
        Roles = [.. roles]
      }
    };

    return (auth, issued);
  }

  public async Task LogoutAsync(
      string? refreshToken, string? accessTokenId, DateTime? accessTokenExpiresAt,
      CancellationToken ct = default)
  {
    // Primero la garantía: sin familia viva, la sesión no se puede extender.
    if (!string.IsNullOrEmpty(refreshToken)
        && await repository.FindByHashAsync(HashOf(refreshToken), ct) is { } stored)
    {
      var revoked = await repository.RevokeFamilyAsync(stored.FamilyId, ct);

      logger.LogInformation(
          "Logout revoked {Count} refresh token(s) of family {FamilyId}", revoked, stored.FamilyId);
    }

    // Y después la optimización: matar ya el access token vigente. Si falla, la sesión sigue cortada.
    if (!string.IsNullOrEmpty(accessTokenId) && accessTokenExpiresAt is { } expiresAt)
      await denylist.RevokeAsync(accessTokenId, expiresAt, ct);
  }

  public async Task RevokeAllSessionsAsync(string userId, CancellationToken ct = default)
  {
    var revoked = await repository.RevokeAllForUserAsync(userId, ct);

    logger.LogInformation("Revoked {Count} session(s) for user {UserId}", revoked, userId);
  }

  /// <summary>Genera un token nuevo y lo deja pendiente de confirmar.</summary>
  private IssuedRefreshToken Create(string userId, Guid familyId, string? clientIp)
  {
    // 32 bytes de un generador criptográfico; un GUID es único pero no impredecible.
    var value = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    var expiresAt = DateTime.Now.Add(_options.Lifetime);

    repository.Add(new RefreshToken
    {
      TokenHash = HashOf(value),
      UserId = userId,
      FamilyId = familyId,
      ExpiresAt = expiresAt,
      CreatedByIp = clientIp
    });

    return new IssuedRefreshToken(value, expiresAt);
  }

  /// <summary>Huella del token: lo único que llega a la base.</summary>
  /// <remarks>
  /// SHA-256 a secas y no un hash de contraseña con sal: el valor ya son 256 bits aleatorios,
  /// así que no hay fuerza bruta que evitar y un algoritmo lento solo añadiría latencia.
  /// </remarks>
  private static string HashOf(string token)
      => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
