using ApiEcommerce.Data;
using ApiEcommerce.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// El inbox: procesar un mensaje <b>exactamente una vez</b>, con la marca y el efecto en
/// la misma transacción.
/// </summary>
/// <remarks>
/// <para>
/// ⭐ <b>Estos son los tests del P0 de la revisión del 2026-09-06</b>, y llevaban desde
/// entonces sin poder escribirse. El bug: la marca se confirmaba <i>antes</i> del efecto,
/// así que si el efecto fallaba, la reentrega se reconocía como duplicado, se hacía ack y
/// el mensaje <b>desaparecía sin procesarse</b>. Se arregló y se verificó a mano que no
/// rompía el camino feliz, pero el caso que de verdad importa —el efecto que revienta— no
/// tenía red.
/// </para>
/// <para>
/// Se podían escribir ahora porque la unidad transaccional salió del <c>BackgroundService</c>
/// a <see cref="IMessageInbox"/>: <b>no hace falta broker</b>, solo un efecto que lance.
/// Mientras vivió dentro del consumidor, probar esto exigía RabbitMQ en la CI.
/// </para>
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
    // ⭐ EL test del P0. Si la marca sobreviviera a un efecto fallido, la reentrega se
    // tomaría por duplicado y el mensaje se perdería en silencio.
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
    // La otra mitad, y la que le da sentido a la anterior: que NO quede marca es un medio;
    // el fin es que el reintento vuelva a ejecutar de verdad. Con el bug original, esta
    // segunda pasada devolvía "duplicado" y el efecto no corría nunca.
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
    // El camino feliz del at-least-once: el broker VA a reentregar, y reprocesar tiene
    // que ser inofensivo.
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
    // Dos réplicas procesando el mismo mensaje a la vez. Quien arbitra es la clave
    // primaria: la que pierde revienta al insertar y su efecto se deshace con ella.
    //
    // ⚠️ Simultáneas de verdad: en secuencia esto lo cumple hasta el atajo del `Any`,
    // que NO es la garantía.
    //
    // Y se afirma algo más que «solo una ejecutó»: que la perdedora **lanza**, y que el
    // inbox sabe reconocer esa excepción como un duplicado. De eso depende el consumidor
    // para hacer ack en vez de gastar un reintento — si `IsConcurrentDuplicate` dejara de
    // reconocerlo, el mensaje daría vueltas hasta la DLQ sin que nada fallara a la vista.
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
