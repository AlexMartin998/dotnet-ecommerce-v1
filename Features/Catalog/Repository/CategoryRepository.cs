using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Repository;



/// <summary>Consultas de categoría que el CRUD genérico no cubre.</summary>
public class CategoryRepository(AppDbContext db) : BaseRepository<Category>(db), ICategoryRepository
{
  public async Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default)
  {
    var normalized = name.Trim();

    var query = _db.Categories
        .Where(c => c.Name == normalized);   // sin funciones sobre la columna: usa IX_Categories_Name

    if (excludeId is int id)
      query = query.Where(c => c.Id != id);

    return await query.AnyAsync(ct);
  }

  public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default)
      => await _db.Categories.AnyAsync(c => c.Slug == slug, ct);

  public async Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default)
      => await _db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Slug == slug, ct);

  public async Task<bool> HasProductsAsync(int categoryId, CancellationToken ct = default)
      => await _db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);

  public async Task<IReadOnlyList<Category>> GetFeaturedAsync(CancellationToken ct = default)
      => await Query()
          .Where(c => c.FeaturedPosition != null)
          .OrderBy(c => c.FeaturedPosition)   // único entre las destacadas: orden total
          .ToListAsync(ct);

  public async Task<int> CountExistingAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
      => await Query().CountAsync(c => ids.Contains(c.Id), ct);

  public async Task ReplaceFeaturedAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default)
  {
    await _db.Database.ExecuteSqlRawAsync("""
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = 'catalog:featured-categories', @LockMode = 'Exclusive',
                                     @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Could not lock the featured categories.', 1;
        """, ct);

    var now = DateTime.Now;

    await _dbSet
        .Where(c => c.FeaturedPosition != null)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(c => c.FeaturedPosition, (int?)null)
            .SetProperty(c => c.UpdatedAt, now), ct);

    for (var index = 0; index < orderedIds.Count; index++)
    {
      var id = orderedIds[index];
      int? position = index + 1;

      await _dbSet
          .Where(c => c.Id == id)
          .ExecuteUpdateAsync(setters => setters
              .SetProperty(c => c.FeaturedPosition, position)
              .SetProperty(c => c.UpdatedAt, now), ct);
    }
  }
}




/* using ApiEcommerce.Data;

namespace ApiEcommerce.Repository;


// primary constructor - DI
public class CategoryRepository(AppDbContext db) : ICategoryRepository
{
  // instancia del contexto
  private readonly AppDbContext _db = db;

  public bool CategoryExists(int id)
  {
    return _db.Categories.Any(c => c.Id == id);
  }

  public bool CategoryExists(string name)
  {
    return _db.Categories.Any(c => c.Name == name.Trim());
  }

  public bool CreateCategory(Category category)
  {
    _db.Categories.Add(category);
    return Save();
  }

  public bool DeleteCategory(Category category)
  {
    _db.Categories.Remove(category);
    return Save();
  }

  public ICollection<Category> GetCategories()
  {
    return _db.Categories.OrderBy(c => c.Id).ToList();
  }

  // repo/domain no lanza excepcion, application si
  public Category? GetCategory(int id)
  {
    return _db.Categories.FirstOrDefault(c => c.Id == id);
  }

  public bool Save()
  {
    return _db.SaveChanges() >= 0;
  }

  public bool UpdateCategory(Category category)
  {
    _db.Categories.Update(category);
    return Save();
  }

}
 */