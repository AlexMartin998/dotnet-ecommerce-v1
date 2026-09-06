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
    var normalized = name.Trim().ToLower();

    var query = _db.Categories
        .Where(c => c.Name.ToLower().Trim() == normalized);

    if (excludeId is int id)
      query = query.Where(c => c.Id != id);

    return await query.AnyAsync(ct);
  }

  public async Task<bool> HasProductsAsync(int categoryId, CancellationToken ct = default)
      => await _db.Products.AnyAsync(p => p.CategoryId == categoryId, ct);
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
    return _db.Categories.Any(c => c.Name.ToLower().Trim() == name.ToLower().Trim());
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