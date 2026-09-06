using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Guarda del banco de pruebas: comprueba que el host de tests está montado contra lo que
/// dice estar montado.
/// </summary>
/// <remarks>
/// Con las implementaciones nulas registradas, los tests de idempotencia pasan en verde
/// sin probar nada: un no-op no rompe ninguna aserción. Esto es la red para que un cambio
/// en cómo se inyecta la configuración no vuelva a hacerlo en silencio.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class TestHostGuardTests(ApiFactory factory)
{
  [Fact]
  public void TheHostRunsAgainstRealRedisAndNotTheNullObject()
  {
    Assert.IsType<RedisIdempotencyStore>(factory.Services.GetRequiredService<IIdempotencyStore>());
    Assert.IsType<RedisCacheService>(factory.Services.GetRequiredService<ICacheService>());
  }

  [Fact]
  public void TheHostRunsAgainstTheTestDatabaseAndNotTheDevelopmentOne()
  {
    // Si esto falla, la suite está borrando y sembrando la base de desarrollo.
    var connectionString = factory.Services
        .GetRequiredService<IConfiguration>().GetConnectionString("ConexionSql");

    Assert.Contains("ApiEcommerceNET8_Tests", connectionString);
  }

  [Fact]
  public void TheRateLimitIsRaisedSoTheSuiteDoesNotThrottleItself()
  {
    var config = factory.Services.GetRequiredService<IConfiguration>();

    Assert.True(int.Parse(config["RateLimit:GlobalPermitLimit"]!) > 1000);
  }
}
