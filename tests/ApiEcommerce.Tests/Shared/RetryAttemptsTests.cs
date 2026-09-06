using System.Text;
using ApiEcommerce.Shared.Messaging.RabbitMq;

namespace ApiEcommerce.Tests.Shared;


/// <summary>
/// El contador de intentos de un mensaje.
/// </summary>
/// <remarks>
/// Unitarios y sin broker: es una función pura, y esa es justo la razón de haberla sacado
/// del consumidor. Mientras vivió dentro de un <c>BackgroundService</c> atado a AMQP,
/// comprobar que sabe leer un valor que llega como <c>byte[]</c> —el error que ya se
/// cometió una vez con <c>x-death</c>— exigía levantar RabbitMQ.
/// </remarks>
public class RetryAttemptsTests
{
  [Fact]
  public void AMessageWithoutTheHeaderHasNotBeenRetried()
  {
    Assert.Equal(0, RetryAttempts.Read(null));
    Assert.Equal(0, RetryAttempts.Read(new Dictionary<string, object?>()));
  }

  [Theory]
  [InlineData(3)]
  [InlineData(0)]
  [InlineData(99)]
  public void TheCounterSurvivesARoundTrip(int attempts)
  {
    var written = RetryAttempts.With(null, attempts);

    Assert.Equal(attempts, RetryAttempts.Read(written));
  }

  [Fact]
  public void AValueTypedByHandInTheBrokerUiIsUnderstood()
  {
    // ⚠️ El bug que ya se cometió con `x-death`: los valores de texto viajan como byte[]
    // en el cliente AMQP, y compararlos sin convertir devuelve false EN SILENCIO. Un
    // operador que ponga la cabecera a mano desde la UI la manda como texto.
    Assert.Equal(7, RetryAttempts.Read(new Dictionary<string, object?>
    {
      [RetryAttempts.HeaderName] = Encoding.UTF8.GetBytes("7")
    }));

    Assert.Equal(7, RetryAttempts.Read(new Dictionary<string, object?>
    {
      [RetryAttempts.HeaderName] = "7"
    }));

    Assert.Equal(7, RetryAttempts.Read(new Dictionary<string, object?>
    {
      [RetryAttempts.HeaderName] = 7L
    }));
  }

  [Fact]
  public void AnUnreadableOrNegativeValueCountsAsNoAttempts()
  {
    // Preferible un reintento de más que descartar un mensaje por no saber leer una
    // cabecera. Y un negativo —que solo puede venir de alguien tocándolo a mano— daría
    // un presupuesto infinito de reintentos.
    Assert.Equal(0, RetryAttempts.Read(new Dictionary<string, object?> { [RetryAttempts.HeaderName] = "no" }));
    Assert.Equal(0, RetryAttempts.Read(new Dictionary<string, object?> { [RetryAttempts.HeaderName] = -5 }));
  }

  [Fact]
  public void OtherHeadersAreKeptAndTheOriginalIsNotMutated()
  {
    // El mensaje que se republica tiene que conservar lo que traía; y el diccionario de
    // entrada es del mensaje que estamos consumiendo, así que no se toca.
    var incoming = new Dictionary<string, object?>
    {
      ["x-correlation-id"] = "abc",
      [RetryAttempts.HeaderName] = 1
    };

    var outgoing = RetryAttempts.With(incoming, 2);

    Assert.Equal("abc", outgoing["x-correlation-id"]);
    Assert.Equal(2, RetryAttempts.Read(outgoing));
    Assert.Equal(1, RetryAttempts.Read(incoming));
  }

  [Fact]
  public void DroppingTheHeaderResetsTheBudget()
  {
    // Es el procedimiento de replay desde la DLQ: borrar UNA cabecera con nombre
    // conocido. Con `x-death` esto era imposible -sobrevive al paso por la DLQ- y un
    // mensaje reencolado por un operador moria en la primera entrega.
    var exhausted = RetryAttempts.With(null, 5);

    exhausted.Remove(RetryAttempts.HeaderName);

    Assert.Equal(0, RetryAttempts.Read(exhausted));
  }
}
