namespace ApiEcommerce.Shared.Messaging.Events;


/// <summary>
/// Contrato de un evento de dominio publicable.
/// </summary>
/// <remarks>
/// <see cref="EventType"/> es el nombre estable que viaja al broker como routing key y es
/// parte del contrato entre servicios: va explícito, no derivado de <c>typeof(T).Name</c>.
/// </remarks>
public interface IDomainEvent
{
  /// <summary>Nombre estable del evento; se usa como routing key.</summary>
  static abstract string EventType { get; }
}
