using ApiEcommerce.Shared.Crud;
namespace ApiEcommerce.Shared.Persistence;


/// <summary>
/// Marca una entidad con clave primaria entera, para que los genéricos
/// (<c>IBaseRepository&lt;T&gt;</c>, <c>ICrudService&lt;...&gt;</c>) lean el <c>Id</c> sin
/// reflexión y restrinjan sus parámetros de tipo.
/// </summary>
public interface IEntity
{
  int Id { get; }
}


/// <summary>
/// Entidad con marcas de tiempo, que <see cref="Data.AppDbContext"/> estampa al guardar.
/// </summary>
/// <remarks>
/// No se asignan a mano en repositorios ni servicios.
/// </remarks>
public interface IAuditable : IEntity
{
  DateTime CreatedAt { get; set; }

  /// <summary>Fecha de la última modificación, o <c>null</c> si nunca se modificó.</summary>
  DateTime? UpdatedAt { get; set; }
}
