using ApiEcommerce.Data;
using ApiEcommerce.Models;

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
