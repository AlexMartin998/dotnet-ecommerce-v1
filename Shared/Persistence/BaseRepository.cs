using ApiEcommerce.Data;
using ApiEcommerce.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Persistence;


/// <summary>
/// Implementación genérica del CRUD sobre <c>DbSet&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// Única herencia del proyecto para reutilizar código: la base es mecanismo puro y las
/// subclases solo agregan consultas de dominio, por eso los métodos no son <c>virtual</c>.
/// No hay unit of work: cada escritura llama a <c>SaveChangesAsync()</c>.
/// </remarks>
public class BaseRepository<T> : IBaseRepository<T> where T : class, IEntity
{
  /// <summary>Contexto de EF Core del request.</summary>
  protected readonly AppDbContext _db;

  /// <summary>Conjunto de la entidad <typeparamref name="T"/>.</summary>
  protected readonly DbSet<T> _dbSet;

  /// <summary>Cacheado: <c>typeof</c> por llamada sería desperdicio en un método caliente.</summary>
  private static readonly bool IsAuditable = typeof(IAuditable).IsAssignableFrom(typeof(T));

  /// <summary>Crea el repositorio sobre el contexto del request.</summary>
  public BaseRepository(AppDbContext db)
  {
    _db = db;
    _dbSet = db.Set<T>();
  }

  /// <summary>
  /// Punto de partida para las consultas de las subclases. Por defecto sin rastreo:
  /// toda lectura que no se va a modificar lleva <c>AsNoTracking()</c>.
  /// </summary>
  protected IQueryable<T> Query(bool tracking = false)
      => tracking ? _dbSet : _dbSet.AsNoTracking();

  /// <summary>
  /// Orden por defecto de los listados: lo más nuevo primero.
  /// </summary>
  /// <remarks>
  /// Usa <c>EF.Property</c> porque un cast a <see cref="IAuditable"/> dentro del árbol de
  /// expresión no es traducible a SQL. El desempate por <c>Id</c> da el orden total sin el
  /// cual paginar está roto: <c>CreatedAt</c> no es único.
  /// </remarks>
  protected IQueryable<T> ApplyDefaultOrder(IQueryable<T> query)
      => IsAuditable
          ? query.OrderByDescending(e => EF.Property<DateTime>(e, nameof(IAuditable.CreatedAt)))
                 .ThenByDescending(e => EF.Property<int>(e, nameof(IEntity.Id)))
          : query.OrderByDescending(e => EF.Property<int>(e, nameof(IEntity.Id)));

  /// <inheritdoc />
  public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
      => await _dbSet.FindAsync([id], ct);

  /// <inheritdoc />
  public async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
      => await ApplyDefaultOrder(Query()).ToListAsync(ct);

  /// <inheritdoc />
  public async Task<PagedResult<T>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
  {
    var ordered = ApplyDefaultOrder(Query());

    // El COUNT va sobre la misma consulta base, para que el total corresponda al mismo
    // filtro que la página.
    var total = await ordered.CountAsync(ct);

    var items = await ordered
        .Skip(PageQuery.SkipFor(page, pageSize))
        .Take(pageSize)
        .ToListAsync(ct);

    return new PagedResult<T>(items, page, pageSize, total);
  }

  /// <inheritdoc />
  public async Task<T> AddAsync(T entity, CancellationToken ct = default)
  {
    await _dbSet.AddAsync(entity, ct);
    await _db.SaveChangesAsync(ct);
    return entity;
  }

  /// <inheritdoc />
  public async Task<T> UpdateAsync(T entity, CancellationToken ct = default)
  {
    // Con la entidad ya rastreada, Update() marcaría todas las columnas como modificadas:
    // solo se adjunta cuando viene desconectada.
    if (_db.Entry(entity).State == EntityState.Detached)
      _dbSet.Update(entity);

    await _db.SaveChangesAsync(ct);
    return entity;
  }

  /// <inheritdoc />
  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    var entity = await GetByIdAsync(id, ct);
    if (entity is null) return;

    _dbSet.Remove(entity);
    await _db.SaveChangesAsync(ct);
  }

  /// <inheritdoc />
  /// <remarks><c>AnyAsync()</c> es un <c>SELECT 1</c>: no materializa ni rastrea la entidad.</remarks>
  public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
      => await _dbSet.AnyAsync(e => EF.Property<int>(e, nameof(IEntity.Id)) == id, ct);

  /// <inheritdoc />
  public async Task SaveChangesAsync(CancellationToken ct = default)
      => await _db.SaveChangesAsync(ct);

  /// <inheritdoc />
  public async Task<bool> ExistsByFieldAsync(
      string fieldName, string value, int? excludeId = null, CancellationToken ct = default)
  {
    var entityType = _db.Model.FindEntityType(typeof(T));
    var property = entityType?.FindProperty(fieldName);

    if (property is null) return false;
    if (property.ClrType != typeof(string)) return false;

    // Traducible a SQL (sin StringComparison, que EF Core no sabe traducir)
    var normalized = value.Trim().ToLower();

    var query = _dbSet.Where(e =>
        EF.Property<string>(e, fieldName).ToLower().Trim() == normalized);

    if (excludeId is int id)
      query = query.Where(e => EF.Property<int>(e, nameof(IEntity.Id)) != id);

    return await query.AnyAsync(ct);
  }
}
