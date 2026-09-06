using System.Net;
using System.Net.Http.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>El endpoint de administración de dead-letters.</summary>
/// <remarks>
/// El host de tests corre sin broker, así que se cubre lo que no depende de él y que un
/// descuido rompe: quién puede llamarlo y qué contesta sin mensajería.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class DeadLetterAdminTests(ApiFactory factory)
{
  [Fact]
  public async Task WithoutABrokerItSaysSoInsteadOfFailingConfusingly()
  {
    // 503 y no 500: no es un fallo, es que la feature no está configurada, y es
    // reintentable.
    using var admin = await factory.AsAdminAsync();

    var response = await admin.GetAsync("/api/v1/dead-letter");

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

    var problem = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    Assert.Equal("broker_unavailable", problem.GetProperty("code").GetString());
  }

  [Fact]
  public async Task A503CarriesRetryAfterBecauseItIsRetryable()
  {
    using var admin = await factory.AsAdminAsync();

    var response = await admin.GetAsync("/api/v1/dead-letter");

    Assert.NotNull(response.Headers.RetryAfter);
  }

  [Fact]
  public async Task MovingMessagesAroundIsNotForRegularUsers()
  {
    // Reencolar es una operación de operador: un usuario normal ni enumera las colas.
    using var user = await factory.AsNewUserAsync();

    Assert.Equal(HttpStatusCode.Forbidden,
        (await user.GetAsync("/api/v1/dead-letter")).StatusCode);

    Assert.Equal(HttpStatusCode.Forbidden,
        (await user.PostAsync("/api/v1/dead-letter/apiecommerce.order-placed/replay?max=1", null)).StatusCode);
  }

  [Fact]
  public async Task WithoutATokenThereIsNothingToSee()
  {
    using var anonymous = factory.Anonymous();

    Assert.Equal(HttpStatusCode.Unauthorized,
        (await anonymous.GetAsync("/api/v1/dead-letter")).StatusCode);
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  [InlineData(501)]
  public async Task TheBatchSizeHasACeiling(int max)
  {
    // Sin techo, un `max` enorme deja la petición moviendo mensajes durante minutos.
    using var admin = await factory.AsAdminAsync();

    var response = await admin.PostAsync(
        $"/api/v1/dead-letter/apiecommerce.order-placed/replay?max={max}", null);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
