using System.Net;
using System.Net.Http.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Versionado por segmento de ruta y sondas: se rompen al añadir un controller y solo se
/// ven levantando la aplicación, porque el enrutado no existe hasta que hay pipeline.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class VersioningAndHealthTests(ApiFactory factory)
{
  [Fact]
  public async Task TheApiStartsMigratesAndSeeds()
  {
    // Cubre de una vez migraciones al arrancar, seeding del admin y validación del
    // contenedor de DI: los tres pueden romper el arranque.
    using var admin = await factory.AsAdminAsync();

    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
  }

  [Fact]
  public async Task TheUnversionedRouteDoesNotExist()
  {
    // Sin versión hay 404 y no v1 por defecto: si no, el día que exista una v2 los
    // clientes viejos cambiarían de comportamiento sin tocar una línea.
    using var client = factory.Anonymous();

    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/category")).StatusCode);
  }

  [Fact]
  public async Task LivenessIsVersionNeutralAndTouchesNoDependency()
  {
    // Sin [ApiVersionNeutral] devuelve 404 y el orquestador mata un proceso sano.
    using var client = factory.Anonymous();

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
  }

  [Fact]
  public async Task ReadinessAnswersAboutItsDependencies()
  {
    using var client = factory.Anonymous();
    var response = await client.GetAsync("/health/ready");

    // Healthy o Degraded, pero nunca 404: la sonda responde algo interpretable.
    Assert.Contains(await response.Content.ReadAsStringAsync(), new[] { "Healthy", "Degraded" });
  }

  [Fact]
  public async Task CreatingReturnsALocationThatIncludesTheVersionAndCanBeFollowed()
  {
    // CreatedAtRoute sobre una ruta versionada necesita el parámetro `version` explícito:
    // sin él revienta al construir el Location y sale un 500.
    using var admin = await factory.AsAdminAsync();

    var created = await admin.PostAsJsonAsync("/api/v1/category", new { name = Unique("Version") });

    Assert.Equal(HttpStatusCode.Created, created.StatusCode);

    var location = created.Headers.Location!.ToString();
    Assert.Contains("/v1/", location);

    // Y el Location tiene que servir de verdad, no solo estar bien formado.
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(location)).StatusCode);
  }

  internal static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];
}
