
using ApiEcommerce.Shared.Persistence;
namespace ApiEcommerce.Shared.Crud;


/// <summary>
/// Reglas de negocio de una entidad, separadas del CRUD. Es el colaborador que
/// <see cref="CrudService{TEntity, TDto, TCreateDto, TUpdateDto}"/> consulta antes de
/// cada escritura.
/// </summary>
/// <remarks>
/// Los métodos son default interface members, así que una entidad implementa solo la
/// regla que necesita. Todo <c>Ensure*</c> comunica el incumplimiento lanzando una
/// <see cref="Exceptions.AppException"/>; nunca devuelve <c>bool</c> ni conoce HTTP.
/// </remarks>
public interface IEntityRules<TEntity, TCreateDto, TUpdateDto> where TEntity : class, IEntity
{

  /// <summary>Nombre de dominio usado en los mensajes de error ("Category", "Product").</summary>
  string EntityName => typeof(TEntity).Name;

  /// <summary>Se ejecuta antes de crear. Lanza si la creación no es válida.</summary>
  Task EnsureCanCreateAsync(TCreateDto dto, CancellationToken ct = default)
      => Task.CompletedTask;

  /// <summary>
  /// Se ejecuta antes de mapear el update sobre <paramref name="existing"/>, de modo
  /// que la regla todavía ve el estado previo de la entidad y de la base.
  /// </summary>
  Task EnsureCanUpdateAsync(int id, TUpdateDto dto, TEntity existing, CancellationToken ct = default)
      => Task.CompletedTask;

  /// <summary>Se ejecuta antes de borrar, con la entidad ya cargada.</summary>
  Task EnsureCanDeleteAsync(TEntity existing, CancellationToken ct = default)
      => Task.CompletedTask;

}
