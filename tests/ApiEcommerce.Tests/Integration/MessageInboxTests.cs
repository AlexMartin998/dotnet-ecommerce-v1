using ApiEcommerce.Data;
using ApiEcommerce.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// El inbox: procesar un mensaje exactamente una vez, con la marca y el efecto en la
/// misma transacción.
/// </summary>
/// <remarks>
/// Si la marca se confirmara antes del efecto, un efecto fallido dejaría el mensaje por
/// duplicado y desaparecería sin procesarse. La unidad transaccional vive en
/// <see cref="IMessageInbox"/> y no en el consumidor, así que no hace falta broker.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class MessageInboxTests(ApiFactory factory)
{
  /// <summary>Un scope por llamada: el inbox es <c>Scoped</c>, como el <c>DbContext</c>.</summary>
  private async Task<T> WithInboxAsync<T>(Func<IMessageInbox, Task<T>> use)
  {
    using var scope = factory.Services.CreateScope();

    return await use(scope.ServiceProvider.GetRequiredService<IMessageInbox>());
  }

  private async Task<bool> IsMarkedAsync(Guid messageId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    return await db.ProcessedMessages.AsNoTracking().AnyAsync(m => m.Id == messageId);
  }

  [Fact]
  public async Task AFailingEffectLeavesNoMarkBehind()
  {
    // Si la marca sobreviviera a un efecto fallido, la reentrega se tomaría por duplicado
    // y el mensaje se perdería en silencio.
    var messageId = Guid.NewGuid();

    var boom = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        WithInboxAsync(inbox => inbox.ProcessOnceAsync(
            messageId, "test.event", _ => throw new InvalidOperationException("efecto roto"))));

    Assert.Equal("efecto roto", boom.Message);

    // La marca se deshizo con el efecto: el mensaje sigue estando "sin procesar".
    Assert.False(await IsMarkedAsync(messageId));
  }

  [Fact]
  public async Task AfterAFailureTheRetryActuallyRunsTheEffectAgain()
  {
    // Que no quede marca es el medio; el fin es que el reintento ejecute de verdad.
    var messageId = Guid.NewGuid();
    var runs = 0;

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        WithInboxAsync(inbox => inbox.ProcessOnceAsync(messageId, "test.event", _ =>
        {
            runs++;
            throw new InvalidOperationException("fallo transitorio");
        })));

    var processed = await WithInboxAsync(inbox =>
        inbox.ProcessOnceAsync(messageId, "test.event", _ => { runs++; return Task.CompletedTask; }));

    Assert.True(processed);
    Assert.Equal(2, runs);
    Assert.True(await IsMarkedAsync(messageId));
  }

  [Fact]
  public async Task ASecondDeliveryOfAProcessedMessageDoesNotRunTheEffect()
  {
    // El broker va a reentregar: reprocesar tiene que ser inofensivo.
    var messageId = Guid.NewGuid();
    var runs = 0;

    var first = await WithInboxAsync(inbox =>
        inbox.ProcessOnceAsync(messageId, "test.event", _ => { runs++; return Task.CompletedTask; }));

    var second = await WithInboxAsync(inbox =>
        inbox.ProcessOnceAsync(messageId, "test.event", _ => { runs++; return Task.CompletedTask; }));

    Assert.True(first);
    Assert.False(second);
    Assert.Equal(1, runs);
  }

  [Fact]
  public async Task OnlyOneOfTwoConcurrentDeliveriesRunsTheEffectAndTheLoserIsRecognisable()
  {
    // Simultáneas, porque en secuencia esto lo cumple hasta el atajo del `Any`. Y la
    // perdedora tiene que lanzar algo reconocible: de eso depende el consumidor para
    // hacer ack en vez de gastar reintentos.
    var messageId = Guid.NewGuid();
    var runs = 0;

    async Task<bool> Deliver()
    {
      return await WithInboxAsync(inbox => inbox.ProcessOnceAsync(messageId, "test.event", async _ =>
      {
        Interlocked.Increment(ref runs);
        // Retenerse dentro de la transacción para que las dos se solapen de verdad.
        await Task.Delay(300);
      }));
    }

    var deliveries = new[] { Deliver(), Deliver() };

    var outcomes = await Task.WhenAll(deliveries.Select(async delivery =>
    {
      try { return (Processed: await delivery, Error: (Exception?)null); }
      catch (Exception ex) { return (Processed: false, Error: ex); }
    }));

    Assert.Single(outcomes, o => o.Processed);
    Assert.True(await IsMarkedAsync(messageId));

    // La que perdió, si perdió por el choque de PK, tiene que ser reconocible como tal.
    var loser = outcomes.SingleOrDefault(o => o.Error is not null);

    if (loser.Error is not null)
    {
      using var scope = factory.Services.CreateScope();
      var inbox = scope.ServiceProvider.GetRequiredService<IMessageInbox>();

      Assert.True(inbox.IsConcurrentDuplicate(loser.Error),
          "El consumidor hace ack basándose en esto; si deja de reconocerlo, el mensaje "
          + "gasta reintentos hasta la DLQ sin que nada falle a la vista.");
    }
  }
}
