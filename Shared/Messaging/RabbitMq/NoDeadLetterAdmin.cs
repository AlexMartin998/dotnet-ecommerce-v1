namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Lo que se registra cuando <b>no hay broker configurado</b>: toda operación falla
/// diciendo exactamente eso.
/// </summary>
/// <remarks>
/// Es un Null Object que falla en CERRADO, al revés que <c>NoCacheService</c> o
/// <c>NoIdempotencyStore</c>: aquí no hay nada que devolver y «no hay mensajes muertos» sería
/// mentira, no degradación. Existe para que el controller no pregunte si hay broker.
/// </remarks>
public sealed class NoDeadLetterAdmin : IDeadLetterAdmin
{
  private const string Message =
      "Messaging is disabled (RabbitMq:ConnectionString is empty), so there are no dead letters to manage.";

  public Task<IReadOnlyList<DeadLetterStatus>> GetStatusAsync(CancellationToken ct = default)
      => throw new BrokerUnavailableException(Message);

  public Task<int> ReplayAsync(string queue, int max, CancellationToken ct = default)
      => throw new BrokerUnavailableException(Message);
}
