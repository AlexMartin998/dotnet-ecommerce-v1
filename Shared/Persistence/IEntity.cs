using ApiEcommerce.Shared.Crud;
namespace ApiEcommerce.Shared.Persistence;


/// <summary>
/// Marca una entidad con clave primaria entera.
/// Permite que los genéricos (<c>IBaseRepository&lt;T&gt;</c>, <c>ICrudService&lt;...&gt;</c>)
/// lean el <c>Id</c> sin recurrir a reflexión y restrinjan sus parámetros de tipo.
/// Equivale al <c>&lt;ID&gt;</c> de <c>JpaRepository&lt;T, ID&gt;</c> de Spring Data.
/// </summary>
public interface IEntity
{
  int Id { get; }
}


/// <summary>
/// Entidad con marcas de tiempo. <see cref="Data.AppDbContext"/> las estampa
/// automáticamente al guardar (equivalente a <c>@CreatedDate</c> / <c>@LastModifiedDate</c>
/// de Spring Data Auditing), así que <b>no se asignan a mano</b> en repositorios ni servicios.
/// </summary>
public interface IAuditable : IEntity
{
  DateTime CreatedAt { get; set; }
  DateTime? UpdatedAt { get; set; }
}
