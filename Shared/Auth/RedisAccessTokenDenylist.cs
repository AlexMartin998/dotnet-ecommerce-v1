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
      // ⚠️ No se propaga: el logout ya revocó la familia en la base, que es la garantía.
      // Lo que se pierde es que el access token actual muera en el acto, y dura como
      // mucho `Jwt:ExpirationMinutes`. Dejar que un fallo de Redis convierta un logout
      // correcto en un 500 sería cambiar una molestia por un error.
      logger.LogWarning(ex,
          "Could not deny-list the access token {TokenId}; the session is revoked but this token lives until it expires",
          tokenId);
    }
  }

  public async Task<bool> IsRevokedAsync(string tokenId, CancellationToken ct = default)
  {
    try
    {
      return await redis.GetDatabase().KeyExistsAsync(_prefix + tokenId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Ante la duda, DEJA PASAR. Corre en cada petición autenticada: fallar en cerrado
      // aquí convierte un corte de Redis en "nadie puede usar la API", que es mucho peor
      // que el riesgo que cubre — un token ya revocado sobreviviendo unos minutos.
      logger.LogWarning(ex, "Access token denylist unavailable; letting the request through");
      return false;
    }
  }
}


/// <summary>Null Object para cuando no hay Redis configurado.</summary>
/// <remarks>
/// Misma decisión que <see cref="RedisAccessTokenDenylist"/> cuando falla: nada queda
/// revocado antes de tiempo, y la sesión se sigue cortando por la base. Sin esto, la app
/// no arrancaría sin Redis.
/// </remarks>
public sealed class NoAccessTokenDenylist : IAccessTokenDenylist
{
  public Task RevokeAsync(string tokenId, DateTime expiresAt, CancellationToken ct = default)
      => Task.CompletedTask;

  public Task<bool> IsRevokedAsync(string tokenId, CancellationToken ct = default)
      => Task.FromResult(false);
}
