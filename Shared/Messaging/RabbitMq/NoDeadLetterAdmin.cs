namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Lo que se registra cuando <b>no hay broker configurado</b>: toda operación falla
/// diciendo exactamente eso.
/// </summary>
/// <remarks>
/// <para>
/// Es un Null Object, como <c>NoCacheService</c> o <c>NoIdempotencyStore</c>, pero con una
/// diferencia importante: los otros <b>degradan en abierto</b> porque envuelven
/// optimizaciones con fuente de verdad alternativa. Aquí no hay nada que devolver — «no hay
/// mensajes muertos» sería <b>mentira</b>, no degradación—, así que falla en cerrado y el
/// operador se entera. Es la distinción de <c>rules.md</c> §8.
/// </para>
/// <para>
/// Existe para que el controller no tenga que preguntar si hay broker: la decisión se toma
/// una vez, al construir el grafo de DI, como con el resto.
/// </para>
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
