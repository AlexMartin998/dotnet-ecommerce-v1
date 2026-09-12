namespace ApiEcommerce.Shared.Crud;


/// <summary>
/// Traducción DTO ↔ entidad de una entidad concreta, que el CRUD compuesto necesita para
/// no tener que mapear por reflexión.
/// </summary>
/// <remarks>
/// Gemelo de <see cref="IEntityRules{TEntity, TCreateDto, TUpdateDto}"/>: el mecanismo es
/// genérico y lo específico de cada entidad entra por el constructor. Sin registro cerrado
/// no hay mapeador, y eso falla al arrancar (<c>ValidateOnBuild</c>), no en la petición.
/// </remarks>
/// <typeparam name="TEntity">La entidad.</typeparam>
/// <typeparam name="TDto">Su DTO de lectura.</typeparam>
/// <typeparam name="TCreateDto">El DTO de creación.</typeparam>
/// <typeparam name="TUpdateDto">El DTO de PATCH, con todos sus campos nullable.</typeparam>
public interface IEntityMapper<TEntity, TDto, TCreateDto, TUpdateDto>
{
  /// <summary>Proyecta la entidad a su DTO de lectura.</summary>
  TDto ToDto(TEntity entity);

  /// <summary>
  /// Construye una entidad nueva. No estampa los campos de auditoría: los pone
  /// <c>AppDbContext.SaveChangesAsync</c>.
  /// </summary>
  TEntity ToEntity(TCreateDto dto);

  /// <summary>
  /// Aplica un PATCH parcial sobre la entidad rastreada: lo que no venga, no se toca.
  /// </summary>
  void Apply(TUpdateDto dto, TEntity entity);
}
