using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Arranque con el seeding apagado y sin contraseña de admin, como en producción.</summary>
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


/// <summary>El arranque: lo que debe tolerar y lo que debe impedir.</summary>
/// <remarks>
/// Una regla condicional no se expresa con un atributo sino con <c>.Validate(...)</c>: un
/// <c>[Required]</c> sobre <c>SeedOptions.AdminPassword</c> se valida aunque el seeding
/// esté apagado, y en producción eso es un crash-loop al arrancar.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class StartupTests
{
  [Fact]
  public async Task ItStartsWithSeedingDisabledAndNoAdminPassword()
  {
    await using var factory = new ProductionLikeFactory();
    using var client = factory.Anonymous();

    // Que responda es todo el test: lo que se comprueba es que llega a levantar.
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
  }

  [Fact]
  public async Task ItRefusesToStartWithoutASigningKey()
  {
    // Una clave ausente no puede degradar en abierto: sin `ValidateOnStart` el proceso
    // arranca y el fallo aparece en el primer login, o firma con una clave vacía.
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
