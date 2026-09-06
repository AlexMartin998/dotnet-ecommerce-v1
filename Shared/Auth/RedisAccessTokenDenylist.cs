using ApiEcommerce.Shared.Caching;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ApiEcommerce.Shared.Auth;


/// <summary>Denylist sobre Redis. Una clave por <c>jti</c>, con TTL igual a su vida restante.</summary>
public sealed class RedisAccessTokenDenylist(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisAccessTokenDenylist> logger) : IAccessTokenDenylist
{
  private readonly string _prefix = cacheOptions.Value.InstanceName + "revoked-jti:";

  /// <inheritdoc />
  public async Task RevokeAsync(string tokenId, DateTime expiresAt, CancellationToken ct = default)
  {
    // Lo que le queda de vida, en UTC porque `exp` de un JWT es epoch UTC (RFC 7519).
    var ttl = expiresAt - DateTime.UtcNow;

    // Ya expirado: la firma no vale por sí sola, no hay nada que revocar.
    if (ttl <= TimeSpan.Zero) return;

    try
    {
      await redis.GetDatabase().StringSetAsync(_prefix + tokenId, "1", ttl);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // No se propaga: el logout ya revocó la familia en la base, que es la garantía. Lo
      // que se pierde es que este access token muera en el acto en vez de al expirar.
      logger.LogWarning(ex,
          "Could not deny-list the access token {TokenId}; the session is revoked but this token lives until it expires",
          tokenId);
    }
  }

  /// <inheritdoc />
  public async Task<bool> IsRevokedAsync(string tokenId, CancellationToken ct = default)
  {
    try
    {
      return await redis.GetDatabase().KeyExistsAsync(_prefix + tokenId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Ante la duda, deja pasar: fallar en cerrado convertiría un corte de Redis en
      // «nadie puede usar la API».
      logger.LogWarning(ex, "Access token denylist unavailable; letting the request through");
      return false;
    }
  }
}


/// <summary>Null Object para cuando no hay Redis configurado.</summary>
/// <remarks>
/// Misma decisión que <see cref="RedisAccessTokenDenylist"/> cuando falla: nada queda
/// revocado antes de tiempo y la sesión se sigue cortando por la base.
/// </remarks>
public sealed class NoAccessTokenDenylist : IAccessTokenDenylist
{
  /// <inheritdoc />
  public Task RevokeAsync(string tokenId, DateTime expiresAt, CancellationToken ct = default)
      => Task.CompletedTask;

  /// <inheritdoc />
  public Task<bool> IsRevokedAsync(string tokenId, CancellationToken ct = default)
      => Task.FromResult(false);
}
