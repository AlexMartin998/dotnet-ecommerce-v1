using System.Net;
using System.Net.Http.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// El endpoint de administración de dead-letters.
/// </summary>
/// <remarks>
/// ⚠️ El host de tests corre <b>sin broker</b>, así que aquí se prueban las dos cosas que no
/// dependen de él y que son las que un descuido rompe: <b>quién puede llamarlo</b> y qué
/// contesta cuando la mensajería no está. El ciclo completo —fallar, morir en la DLQ,
/// reemitir y ver la orden recuperarse— se verificó ejecutando contra un RabbitMQ real
/// (<c>planning/21</c> §21.4).
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class DeadLetterAdminTests(ApiFactory factory)
{
  [Fact]
  public async Task WithoutABrokerItSaysSoInsteadOfFailingConfusingly()
  {
    // 503 y no 500: no es un fallo nuestro ni del cliente, es que la feature no está
    // configurada. Y 503 es reintentable, que es exactamente el caso.
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
    // Reencolar mensajes en un broker es una operación de operador. Un usuario normal no
    // debería siquiera poder enumerar las colas.
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
    // Sin techo, un `max` enorme deja la petición HTTP moviendo mensajes de uno en uno
    // durante minutos. Quien necesite más, llama otra vez.
    using var admin = await factory.AsAdminAsync();

    var response = await admin.PostAsync(
        $"/api/v1/dead-letter/apiecommerce.order-placed/replay?max={max}", null);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }
}
