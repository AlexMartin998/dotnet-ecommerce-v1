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

    // ---- detección de reuso ------------------------------------------------
    // Que un token YA GASTADO vuelva a aparecer solo tiene dos explicaciones: o hay dos
    // copias circulando (robo), o el propio cliente lanzó dos refrescos a la vez. La
    // rotación existe precisamente para que esto sea distinguible.
    if (stored.RevokedAt is { } revokedAt)
    {
      // ⚠️ La ventana de gracia no es un parche: sin ella la detección es inutilizable.
      // Un móvil o una SPA lanzan varias peticiones en paralelo; si dos reciben 401 casi
      // a la vez, las dos refrescan con el mismo token y la segunda parece un ladrón.
      // Dentro de la ventana se rechaza igual —ese token está gastado— pero NO se revoca
      // la familia, así que el token nuevo que ya recibió la otra petición sigue valiendo.
      if (now - revokedAt <= _options.ReuseGrace)
      {
        logger.LogInformation(
            "Refresh token for user {UserId} was already rotated {Ago:0.0}s ago; treating it as a client race",
            stored.UserId, (now - revokedAt).TotalSeconds);

        throw new UnauthorizedAppException("Invalid refresh token.");
      }

      // Fuera de la ventana sí es señal de robo: no se sabe cuál de las dos copias es la
      // del dueño, así que se cae la sesión entera. Duro a propósito.
      var revoked = await repository.RevokeFamilyAsync(stored.FamilyId, ct);

      logger.LogWarning(
          "Refresh token REUSE detected for user {UserId}; revoked {Count} token(s) of family {FamilyId}",
          stored.UserId, revoked, stored.FamilyId);

      throw new UnauthorizedAppException("Invalid refresh token.");
    }

    if (stored.ExpiresAt <= now)
      throw new UnauthorizedAppException("Invalid refresh token.");

    var user = await userManager.FindByIdAsync(stored.UserId)
        // La cuenta desapareció con la sesión abierta. No es 404: para quien pregunta,
        // su credencial ya no vale, y decir "ese usuario no existe" es un oráculo.
        ?? throw new UnauthorizedAppException("Invalid refresh token.");

    // ⚠️ Renovar NO vuelve a pedir credenciales, así que no pasa por el bloqueo de
    // Identity: sin esta comprobación, una cuenta bloqueada podría seguir renovando su
    // sesión **indefinidamente** y el bloqueo no serviría de nada. Es el mismo motivo por
    // el que bloquear revoca además las sesiones abiertas — esto es el cinturón, aquello
    // los tirantes: cubre también a un usuario que se bloquee solo por fallar el login.
    if (await userManager.IsLockedOutAsync(user))
    {
      logger.LogWarning("Refresh refused for locked-out user {UserId}", stored.UserId);
      throw new UnauthorizedAppException("Invalid refresh token.");
    }

    var roles = await userManager.GetRolesAsync(user);

    // Gastar el viejo y emitir el nuevo, ATÓMICO. Si solo se confirmara lo primero, el
    // usuario se quedaría sin sesión por un fallo nuestro.
    var issued = await transactions.ExecuteAsync(async token =>
    {
      // El UPDATE condicional es quien arbitra: si dos peticiones simultáneas llegan
      // hasta aquí con el mismo token, solo una lo gasta. La otra recibe `false`.
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
    // Primero la GARANTÍA: sin familia viva, la sesión no se puede extender.
    if (!string.IsNullOrEmpty(refreshToken)
        && await repository.FindByHashAsync(HashOf(refreshToken), ct) is { } stored)
    {
      var revoked = await repository.RevokeFamilyAsync(stored.FamilyId, ct);

      logger.LogInformation(
          "Logout revoked {Count} refresh token(s) of family {FamilyId}", revoked, stored.FamilyId);
    }

    // Y después la OPTIMIZACIÓN: que el access token que el cliente ya tiene muera ahora
    // en vez de al expirar. Si esto falla, la sesión sigue cortada igual.
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
    // 32 bytes de un generador criptográfico. Un GUID NO vale: sus bits no son todos
    // aleatorios y su propósito es ser único, no impredecible.
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
  /// SHA-256 a secas y no un hash de contraseña con sal: aquí el valor ya son 256 bits
  /// aleatorios, así que no hay nada que adivinar por fuerza bruta y un algoritmo lento
  /// solo añadiría latencia a cada refresh. La sal tampoco aporta: no hay dos usuarios
  /// que puedan tener "el mismo token".
  /// </remarks>
  private static string HashOf(string token)
      => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)));
}
