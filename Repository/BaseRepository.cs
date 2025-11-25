using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Repository;


public class BaseRepository<T> : IBaseRepository<T> where T : class
{
  protected readonly AppDbContext _db;
  protected readonly DbSet<T> _dbSet;

  public BaseRepository(AppDbContext db)
  {
    _db = db;
    _dbSet = db.Set<T>();
  }

  public async Task<T?> GetByIdAsync(int id)
      => await _dbSet.FindAsync(id);

  public async Task<IEnumerable<T>> GetAllAsync()
      => await _dbSet.AsNoTracking().ToListAsync();

  public async Task<T> AddAsync(T entity)
  {
    await _dbSet.AddAsync(entity);
    await _db.SaveChangesAsync();
    return entity;
  }

  public async Task<T> UpdateAsync(T entity)
  {
    _dbSet.Update(entity);
    await _db.SaveChangesAsync();
    return entity;
  }

  public async Task DeleteAsync(int id)
  {
    var entity = await GetByIdAsync(id);
    if (entity is null) return;

    _dbSet.Remove(entity);
    await _db.SaveChangesAsync();
  }

  public async Task<bool> ExistsAsync(int id)
      => await _dbSet.FindAsync(id) is not null;

  public async Task<bool> ExistsByFieldAsync(string fieldName, string value)
  {
    var entityType = _db.Model.FindEntityType(typeof(T));
    var property = entityType?.FindProperty(fieldName);

    if (property is null) return false;
    if (property.ClrType != typeof(string)) return false;

    // Traducible a SQL (sin StringComparison)
    var lowered = value.ToLower();
    return await _dbSet.AnyAsync(e =>
        EF.Property<string>(e, fieldName).ToLower() == lowered);
  }
}
