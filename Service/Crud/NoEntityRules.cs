using ApiEcommerce.Models;

namespace ApiEcommerce.Service.Crud;


/// <summary>
/// Reglas vacías: se registra como genérico abierto en DI para que una entidad sin
/// reglas propias funcione sin escribir una sola línea. Las entidades que sí tienen
/// reglas registran su implementación cerrada, que gana sobre este genérico.
/// </summary>
public sealed class NoEntityRules<TEntity, TCreateDto, TUpdateDto>
  : IEntityRules<TEntity, TCreateDto, TUpdateDto>
  where TEntity : class, IEntity
{
}
