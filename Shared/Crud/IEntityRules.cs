
using ApiEcommerce.Shared.Persistence;
namespace ApiEcommerce.Shared.Crud;


/// <summary>
/// Reglas de negocio de una entidad, <b>separadas del CRUD</b>. Es el colaborador que
/// <see cref="CrudService{TEntity, TDto, TCreateDto, TUpdateDto}"/> consulta antes de
/// cada escritura.
/// </summary>
/// <remarks>
/// <para>
/// Sustituye a los hooks <c>OnBeforeCreateAsync</c> / <c>OnBeforeUpdateAsync</c> de una
/// clase base abstracta. La diferencia práctica: estas reglas se pueden instanciar y
/// probar solas (solo dependen del repositorio), se pueden reutilizar o decorar, y no
/// existe forma de que una entidad "se salte" el CRUD sobreescribiéndolo.
/// </para>
/// <para>
/// Los métodos tienen <b>implementación por defecto vacía</b> (default interface
/// members, C# 8+): una entidad sin reglas propias no escribe nada, y una entidad con
/// una sola regla implementa solo ese método.
/// </para>
/// <para>
/// Todo método <c>Ensure*</c> comunica el incumplimiento lanzando una
/// <see cref="Exceptions.AppException"/> (<c>ConflictAppException</c>,
/// <c>BadOperationAppException</c>…). Nunca devuelve <c>bool</c> ni conoce HTTP.
/// </para>
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
