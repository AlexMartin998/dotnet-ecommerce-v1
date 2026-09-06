using System.Text;
using ApiEcommerce.Shared.Messaging.RabbitMq;

namespace ApiEcommerce.Tests.Shared;


/// <summary>El contador de intentos de un mensaje.</summary>
/// <remarks>
/// Unitarios y sin broker: es una función pura, y esa es la razón de haberla sacado del
/// consumidor.
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
    // Los valores de texto viajan como byte[] en el cliente AMQP y compararlos sin
    // convertir da false en silencio; desde la UI del broker llegan como texto.
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
    // Mejor un reintento de más que descartar un mensaje por no saber leer una cabecera;
    // y un negativo daría un presupuesto infinito de reintentos.
    Assert.Equal(0, RetryAttempts.Read(new Dictionary<string, object?> { [RetryAttempts.HeaderName] = "no" }));
    Assert.Equal(0, RetryAttempts.Read(new Dictionary<string, object?> { [RetryAttempts.HeaderName] = -5 }));
  }

  [Fact]
  public void OtherHeadersAreKeptAndTheOriginalIsNotMutated()
  {
    // El republicado conserva lo que traía, y el diccionario de entrada es del mensaje
    // que se está consumiendo, así que no se toca.
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
    // Es el procedimiento de replay desde la DLQ: borrar una cabecera con nombre conocido,
    // cosa que `x-death` no permite porque sobrevive al paso por la DLQ.
    var exhausted = RetryAttempts.With(null, 5);

    exhausted.Remove(RetryAttempts.HeaderName);

    Assert.Equal(0, RetryAttempts.Read(exhausted));
  }
}
