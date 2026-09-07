using ApiEcommerce.Data;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Idempotency;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// La envoltura de idempotencia: la marca del intento y el efecto, en la misma transacción.
/// </summary>
/// <remarks>
/// Gemelo de <see cref="MessageInboxTests"/>. Se prueba aquí y no por cada caso de uso
/// porque desde que la envoltura es una pieza propia hay un único sitio donde puede
/// romperse la garantía.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class IdempotentCommandRunnerTests(ApiFactory factory)
{
  private sealed record Command(string Sku, int Quantity);

  private sealed class Result { public string Value { get; init; } = string.Empty; }

  private static CommandIntent AnIntent() => new("tests.command", Guid.NewGuid().ToString());

  /// <summary>Un scope por llamada: el runner es <c>Scoped</c>, como el <c>DbContext</c>.</summary>
  private async Task<T> WithRunnerAsync<T>(Func<IIdempotentCommandRunner, Task<T>> use)
  {
    using var scope = factory.Services.CreateScope();

    return await use(scope.ServiceProvider.GetRequiredService<IIdempotentCommandRunner>());
  }

  private async Task<bool> IsRecordedAsync(CommandIntent intent)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    return await db.ExecutedCommands.AsNoTracking().AnyAsync(c => c.Id == intent.StorageKey);
  }

  [Fact]
  public async Task TheFirstRunExecutesAndIsNotAReplay()
  {
    var intent = AnIntent();

    var outcome = await WithRunnerAsync(r => r.RunAsync(
        intent, new Command("SKU-1", 1), _ => Task.FromResult(new Result { Value = "hecho" })));

    Assert.False(outcome.WasReplayed);
    Assert.Equal("hecho", outcome.Result.Value);
    Assert.True(await IsRecordedAsync(intent));
  }

  [Fact]
  public async Task TheSameIntentTwiceRunsTheEffectOnlyOnce()
  {
    var intent = AnIntent();
    var command = new Command("SKU-1", 1);
    var runs = 0;

    Task<Result> Effect(CancellationToken _)
    {
      runs++;
      return Task.FromResult(new Result { Value = $"ejecucion {runs}" });
    }

    await WithRunnerAsync(r => r.RunAsync(intent, command, Effect));
    var second = await WithRunnerAsync(r => r.RunAsync(intent, command, Effect));

    Assert.Equal(1, runs);
    Assert.True(second.WasReplayed);

    // Devuelve el resultado GUARDADO, no uno nuevo: es lo que hace que el reintento del
    // cliente vea exactamente la misma respuesta.
    Assert.Equal("ejecucion 1", second.Result.Value);
  }

  [Fact]
  public async Task AFailingEffectLeavesNoMarkBehind()
  {
    // Si la marca sobreviviera al fallo, el reintento del cliente diría "ya está hecho"
    // sin que se hubiera hecho nada. Es la razón de que la envoltura exista.
    var intent = AnIntent();

    var boom = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        WithRunnerAsync(r => r.RunAsync<Result>(
            intent, new Command("SKU-1", 1),
            _ => throw new InvalidOperationException("efecto roto"))));

    Assert.Equal("efecto roto", boom.Message);
    Assert.False(await IsRecordedAsync(intent));
  }

  [Fact]
  public async Task AfterAFailureTheRetryActuallyRuns()
  {
    var intent = AnIntent();
    var command = new Command("SKU-1", 1);
    var runs = 0;

    await Assert.ThrowsAsync<InvalidOperationException>(() =>
        WithRunnerAsync(r => r.RunAsync<Result>(intent, command, _ =>
        {
          runs++;
          throw new InvalidOperationException("fallo transitorio");
        })));

    var outcome = await WithRunnerAsync(r => r.RunAsync(intent, command, _ =>
    {
      runs++;
      return Task.FromResult(new Result { Value = "a la segunda" });
    }));

    Assert.Equal(2, runs);
    Assert.False(outcome.WasReplayed);
    Assert.Equal("a la segunda", outcome.Result.Value);
  }

  [Fact]
  public async Task TheSameKeyWithADifferentBodyIsRejected()
  {
    // Reutilizar la clave con otro cuerpo no es un reintento: es otro comando disfrazado.
    var intent = AnIntent();

    await WithRunnerAsync(r => r.RunAsync(
        intent, new Command("SKU-1", 1), _ => Task.FromResult(new Result())));

    await Assert.ThrowsAsync<IdempotencyConflictAppException>(() =>
        WithRunnerAsync(r => r.RunAsync(
            intent, new Command("SKU-1", 99), _ => Task.FromResult(new Result()))));
  }

  [Fact]
  public async Task WithoutAnIntentTheEffectRunsEveryTime()
  {
    // Renunciar a la garantía lo decide el cliente no mandando Idempotency-Key, y entonces
    // esto tiene que comportarse como si la envoltura no existiera.
    var command = new Command("SKU-1", 1);
    var runs = 0;

    Task<Result> Effect(CancellationToken _)
    {
      runs++;
      return Task.FromResult(new Result());
    }

    await WithRunnerAsync(r => r.RunAsync(CommandIntent.None, command, Effect));
    var second = await WithRunnerAsync(r => r.RunAsync(CommandIntent.None, command, Effect));

    Assert.Equal(2, runs);
    Assert.False(second.WasReplayed);
  }
}
