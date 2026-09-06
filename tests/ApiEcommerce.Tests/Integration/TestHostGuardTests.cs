using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Idempotency;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Guarda del propio banco de pruebas: comprueba que el host de tests está montado
/// contra lo que dice estar montado.
/// </summary>
/// <remarks>
/// <para>
/// No es paranoia, es una cicatriz. Con la configuración inyectada por
/// <c>ConfigureAppConfiguration</c>, el host arrancaba con
/// <c>NoIdempotencyStore</c> y <c>NoCacheService</c>: todos los tests de idempotencia
/// <b>pasaban en verde sin probar absolutamente nada</b>, porque un no-op no rompe una
/// aserción de "dos peticiones distintas dan dos resultados". Solo cayeron los dos que
/// exigían un replay de verdad.
/// </para>
/// <para>
/// ⚠️ La causa: <c>AddDistributedCaching</c> lee la configuración de forma <b>eager</b>
/// para decidir QUÉ implementación registra, y eso ocurre mientras corre <c>Program</c>
/// — <b>antes</b> de que se apliquen los callbacks de <c>ConfigureAppConfiguration</c>.
/// <c>UseSetting</c> sí entra antes. Este test es la red para que no vuelva a pasar en
/// silencio.
/// </para>
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
    // Si esto falla, la suite está borrando y sembrando la base de trabajo del autor.
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
