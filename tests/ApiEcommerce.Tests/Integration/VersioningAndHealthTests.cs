using System.Net;
using System.Net.Http.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Versionado por segmento de ruta y sondas. Son las dos cosas que se rompen al añadir un
/// controller y que <b>solo</b> se ven levantando la aplicación: el enrutado no existe
/// hasta que hay pipeline.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class VersioningAndHealthTests(ApiFactory factory)
{
  [Fact]
  public async Task TheApiStartsMigratesAndSeeds()
  {
    // Si esto pasa, ya se probó de verdad: migraciones aplicadas al arrancar (la imagen
    // de runtime no lleva dotnet-ef), el admin sembrado por RoleManager/UserManager, y
    // el contenedor de DI validado. Los tres han roto el arranque alguna vez.
    using var admin = await factory.AsAdminAsync();

    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
  }

  [Fact]
  public async Task TheUnversionedRouteDoesNotExist()
  {
    // Todo vive bajo /api/v1. Un cliente que se olvide de la versión recibe 404, no la v1
    // por defecto: sin esto, el día que exista una v2 los clientes viejos cambiarían de
    // comportamiento sin tocar una línea.
    using var client = factory.Anonymous();

    Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/category")).StatusCode);
  }

  [Fact]
  public async Task LivenessIsVersionNeutralAndTouchesNoDependency()
  {
    // /health lo sirve un controller con [ApiVersionNeutral]: sin ese atributo devuelve
    // 404 y el orquestador mata un proceso perfectamente sano.
    using var client = factory.Anonymous();

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
  }

  [Fact]
  public async Task ReadinessAnswersAboutItsDependencies()
  {
    using var client = factory.Anonymous();
    var response = await client.GetAsync("/health/ready");

    // Healthy o Degraded, pero nunca 404: la sonda existe y responde algo interpretable.
    Assert.Contains(await response.Content.ReadAsStringAsync(), new[] { "Healthy", "Degraded" });
  }

  [Fact]
  public async Task CreatingReturnsALocationThatIncludesTheVersionAndCanBeFollowed()
  {
    // ⚠️ CreatedAtRoute sobre una ruta versionada necesita el parámetro `version`
    // explícito. Sin él revienta al construir el Location y sale un 500 sin relación
    // aparente con lo que se pedía.
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
