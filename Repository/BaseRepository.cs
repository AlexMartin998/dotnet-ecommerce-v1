using ApiEcommerce.Data;
using ApiEcommerce.Models;
using ApiEcommerce.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Repository;


/// <summary>
/// Implementación genérica del CRUD sobre <c>DbSet&lt;T&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// Esta es la <b>única</b> herencia que el proyecto usa para reutilizar código, y es
/// deliberada: la base es mecanismo puro (acceso a datos, sin ninguna regla de
/// negocio) y las subclases solo <b>agregan</b> consultas de dominio, nunca cambian
/// el CRUD. Por eso los métodos <b>no son <c>virtual</c></b>: nadie puede alterar en
/// silencio la semántica documentada en <see cref="IBaseRepository{T}"/>.
/// La política (reglas de negocio) se compone en la capa de servicio; ver
/// <c>AGENTS/docs/03-service.md</c>.
/// </para>
/// <para>
/// No hay unit of work: cada escritura llama a <c>SaveChangesAsync()</c>. Para
/// atomicidad entre varios repositorios se usa <c>[Transactional]</c> en la acción.
/// </para>
/// </remarks>
public class BaseRepository<T> : IBaseRepository<T> where T : class, IEntity
{
  protected readonly AppDbContext _db;
  protected readonly DbSet<T> _dbSet;

  /// <summary>Cacheado: <c>typeof</c> por llamada sería desperdicio en un método caliente.</summary>
  private static readonly bool IsAuditable = typeof(IAuditable).IsAssignableFrom(typeof(T));

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
  /// Usa <c>EF.Property</c> en vez de un cast a <see cref="IAuditable"/> porque un
  /// cast dentro del árbol de expresión no es traducible a SQL.
  /// </summary>
  protected IQueryable<T> ApplyDefaultOrder(IQueryable<T> query)
      => IsAuditable
          ? query.OrderByDescending(e => EF.Property<DateTime>(e, nameof(IAuditable.CreatedAt)))
          : query.OrderByDescending(e => EF.Property<int>(e, nameof(IEntity.Id)));

  public async Task<T?> GetByIdAsync(int id, CancellationToken ct = default)
      => await _dbSet.FindAsync([id], ct);

  public async Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default)
      => await ApplyDefaultOrder(Query()).ToListAsync(ct);

  public async Task<PagedResult<T>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default)
  {
    var ordered = ApplyDefaultOrder(Query());

    // El COUNT va primero y sobre la misma consulta base: así el total corresponde
    // al mismo filtro que la página (aquí no hay filtro, pero la forma se mantiene
    // para cuando lo haya).
    var total = await ordered.CountAsync(ct);

    var items = await ordered
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);

    return new PagedResult<T>(items, page, pageSize, total);
  }

  public async Task<T> AddAsync(T entity, CancellationToken ct = default)
  {
    await _dbSet.AddAsync(entity, ct);
    await _db.SaveChangesAsync(ct);
    return entity;
  }

  public async Task<T> UpdateAsync(T entity, CancellationToken ct = default)
  {
    // Si la entidad ya viene rastreada (el caso normal: GetByIdAsync + Map encima),
    // llamar a Update() marcaría TODAS las columnas como modificadas y generaría un
    // UPDATE de la fila entera. Solo se adjunta cuando viene desconectada.
    if (_db.Entry(entity).State == EntityState.Detached)
      _dbSet.Update(entity);

    await _db.SaveChangesAsync(ct);
    return entity;
  }

  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    var entity = await GetByIdAsync(id, ct);
    if (entity is null) return;

    _dbSet.Remove(entity);
    await _db.SaveChangesAsync(ct);
  }

  // AnyAsync() es un `SELECT 1`: no materializa ni rastrea la entidad,
  // a diferencia del FindAsync() que se usaba antes.
  public async Task<bool> ExistsAsync(int id, CancellationToken ct = default)
      => await _dbSet.AnyAsync(e => EF.Property<int>(e, nameof(IEntity.Id)) == id, ct);

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
