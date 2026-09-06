using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Arranque con el seeding APAGADO y sin contraseña de admin, como en producción.</summary>
public sealed class ProductionLikeFactory : ApiFactory
{
  protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
  {
    ["Seed:Enabled"] = "false",
    ["Seed:AdminUsername"] = null,
    ["Seed:AdminEmail"] = null,
    ["Seed:AdminPassword"] = null,
  };
}


/// <summary>Arranque sin la clave de firma del JWT.</summary>
public sealed class NoJwtSecretFactory : ApiFactory
{
  protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
  {
    ["Jwt:SecretKey"] = ""
  };
}


/// <summary>
/// El arranque. Estos dos tests cubren un P0 real y su reverso.
/// </summary>
/// <remarks>
/// El P0: un <c>[Required]</c> sobre <c>SeedOptions.AdminPassword</c> se validaba
/// <b>siempre que alguien leyera <c>.Value</c></b>, aunque el seeding estuviera apagado.
/// Un despliegue en producción sin esa variable moría con
/// <c>OptionsValidationException</c> y, con <c>restart: unless-stopped</c>, entraba en
/// <b>crash-loop</b>. La lección: una regla CONDICIONAL no se expresa con un atributo,
/// va en <c>.Validate(...)</c>.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class StartupTests
{
  [Fact]
  public async Task ItStartsWithSeedingDisabledAndNoAdminPassword()
  {
    await using var factory = new ProductionLikeFactory();
    using var client = factory.Anonymous();

    // Que responda es todo el test: antes ni llegaba a levantar.
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
  }

  [Fact]
  public async Task ItRefusesToStartWithoutASigningKey()
  {
    // El reverso, y no es simetría: una clave ausente NO puede degradar en abierto.
    // Sin `ValidateOnStart` el proceso arrancaría tan feliz y el fallo aparecería en el
    // primer login — o peor, firmaría con una clave vacía.
    await using var factory = new NoJwtSecretFactory();

    var exception = Record.Exception(() => factory.Anonymous());

    Assert.NotNull(exception);
    Assert.Contains(Flatten(exception!), m => m.Contains("Jwt", StringComparison.OrdinalIgnoreCase)
                                           || m.Contains("SecretKey", StringComparison.OrdinalIgnoreCase));
  }

  private static IEnumerable<string> Flatten(Exception exception)
  {
    for (var current = exception; current is not null; current = current.InnerException)
      yield return current.Message;
  }
}
