using ApiEcommerce.Data;
using ApiEcommerce.Models;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Repository;


public class ProductRepository(AppDbContext db)
    : BaseRepository<Product>(db), IProductRepository
{


  public async Task<ICollection<Product>> GetProductsForCategory(int categoryId)
  {
    return await _db.Products
        .AsNoTracking()
        .Where(p => p.CategoryId == categoryId)
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync();
  }

  public async Task<ICollection<Product>> SearchProduct(string name)
  {
    if (string.IsNullOrWhiteSpace(name)) return Array.Empty<Product>();

    var pattern = $"%{name.Trim()}%";

    return await _db.Products
        .AsNoTracking()
        .Where(p => EF.Functions.Like(p.Name, pattern))
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync();
  }

  public async Task<bool> BuyProduct(string sku, int quantity)
  {
    if (string.IsNullOrWhiteSpace(sku) || quantity <= 0) return false;

    var normalizedSku = sku.ToLower().Trim();
    var product = await _db.Products.FirstOrDefaultAsync(
        p => p.SKU.ToLower().Trim() == normalizedSku);

    if (product is null || product.Stock < quantity) return false;

    product.Stock -= quantity;
    product.UpdatedAt = DateTime.Now;

    _db.Products.Update(product);
    await _db.SaveChangesAsync();

    return true;
  }
}




// using ApiEcommerce.Data;
// using ApiEcommerce.Models;
// using Microsoft.EntityFrameworkCore;

// namespace ApiEcommerce.Repository;


// // primary constructor - DI
// public class ProductRepository(AppDbContext db) : IProductRepository
// {

//   private readonly AppDbContext _db = db;




//   public async Task<ICollection<Product>> GetProducts()
//   {
//     var productList = await _db.Products
//       .AsNoTracking() // mejora performance - solo lectura sin ORM funcionalidad
//       .OrderByDescending(p => p.CreatedAt)
//       .ToListAsync();

//     return productList;
//   }

//   public async Task<Product?> GetProduct(int id)
//   {
//     return await _db.Products
//       .AsNoTracking()
//       .FirstOrDefaultAsync(p => p.Id == id);
//   }

//   public async Task<ICollection<Product>> GetProductsForCategory(int categoryId)
//   {
//     var list = await _db.Products
//       .AsNoTracking()
//       .Where(p => p.CategoryId == categoryId)
//       .OrderByDescending(p => p.CreatedAt)
//       .ToListAsync();

//     return list;
//   }

//   public async Task<bool> ProductExists(int id)
//   {
//     return await _db.Products.AnyAsync(p => p.Id == id);
//   }

//   public async Task<bool> ProductExists(string name)
//   {
//     return await _db.Products.AnyAsync(p => p.Name.ToLower().Trim() == name.ToLower().Trim());
//   }


//   public async Task<ICollection<Product>> SearchProduct(string name)
//   {
//     if (string.IsNullOrWhiteSpace(name)) return Array.Empty<Product>();

//     var pattern = $"%{name.Trim()}%";

//     var list = await _db.Products
//       .AsNoTracking()
//       .Where(p => EF.Functions.Like(p.Name, pattern))
//       .OrderByDescending(p => p.CreatedAt)
//       .ToListAsync();

//     return list;
//   }

//   public async Task<bool> CreateProduct(Product product)
//   {
//     await _db.Products.AddAsync(product);
//     return await Save();
//   }

//   public async Task<bool> UpdateProduct(Product product)
//   {
//     product.UpdatedAt = DateTime.Now;
//     _db.Products.Update(product);
//     return await Save();
//   }

//   public async Task<bool> DeleteProduct(Product product)
//   {
//     _db.Products.Remove(product);
//     return await Save();
//   }


//   public async Task<bool> BuyProduct(string sku, int quantity)
//   {
//     if (string.IsNullOrWhiteSpace(sku) || quantity <= 0) return false;

//     var normalizedSku = sku.ToLower().Trim();
//     var product = await _db.Products.FirstOrDefaultAsync(p => p.SKU.ToLower().Trim() == normalizedSku);

//     if (product == null || product.Stock < quantity) return false;

//     product.Stock -= quantity;
//     product.UpdatedAt = DateTime.Now;

//     _db.Products.Update(product);

//     return await Save();
//   }


//   public async Task<bool> Save()
//   {
//     return await _db.SaveChangesAsync() >= 0;
//   }


// }
