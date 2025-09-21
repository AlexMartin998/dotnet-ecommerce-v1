using ApiEcommerce.Models;

namespace ApiEcommerce.Repository;


public interface ICategoryRepository
{

  ICollection<Category> GetCategories();

  Category? GetCategory(int id);

  // esto es sobre carga de metodos
  bool CategoryExists(int id);
  bool CategoryExists(string name);

  bool CreateCategory(Category category);

  bool UpdateCategory(Category category);

  bool DeleteCategory(Category category);

  bool Save(); // changes in database


}
