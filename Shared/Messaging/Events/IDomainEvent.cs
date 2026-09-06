namespace ApiEcommerce.Shared.Messaging.Events;


/// <summary>
/// Contrato de un evento de dominio publicable.
/// </summary>
/// <remarks>
/// El <see cref="EventType"/> es el nombre estable que viaja al broker y se usa como
/// routing key. Es parte del <b>contrato público</b> entre servicios: renombrar la
/// clase C# no debe romper a los consumidores, así que el nombre va explícito y no se
/// deriva de <c>typeof(T).Name</c>.
/// </remarks>
public interface IDomainEvent
{
  static abstract string EventType { get; }
}
