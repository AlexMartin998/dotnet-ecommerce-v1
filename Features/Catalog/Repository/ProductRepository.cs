using ApiEcommerce.Data;
using ApiEcommerce.Shared.Paging;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Repository;


/// <summary>Consultas de producto que el CRUD genérico no cubre.</summary>
public class ProductRepository(AppDbContext db)
    : BaseRepository<Product>(db), IProductRepository
{


  public async Task<IEnumerable<Product>> GetAllWithCategoryAsync(CancellationToken ct = default)
  {
    return await Query()
        .Include(p => p.Category)
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync(ct);
  }

  public async Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken ct = default)
  {
    return await Query()
        .Include(p => p.Category)
        .FirstOrDefaultAsync(p => p.Id == id, ct);
  }

  public async Task<PagedResult<Product>> GetPagedWithCategoryAsync(
      int page, int pageSize, CancellationToken ct = default)
  {
    var ordered = Query()
        .Include(p => p.Category)
        .OrderByDescending(p => p.CreatedAt)
        .ThenByDescending(p => p.Id);   // desempate: sin él, dos productos creados en el
                                        // mismo tick pueden repetirse entre páginas.

    // El COUNT va sobre la misma consulta base que la página, para que TotalItems siga
    // siendo cierto el día que se añada un Where.
    var total = await ordered.CountAsync(ct);

    var items = await ordered
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToListAsync(ct);

    return new PagedResult<Product>(items, page, pageSize, total);
  }

  public async Task<ICollection<Product>> GetProductsForCategoryAsync(int categoryId, CancellationToken ct = default)
  {
    return await Query()
        .Include(p => p.Category)
        .Where(p => p.CategoryId == categoryId)
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync(ct);
  }

  public async Task<ICollection<Product>> SearchProductAsync(string name, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(name)) return [];

    var pattern = $"%{name.Trim()}%";

    return await Query()
        .Include(p => p.Category)
        .Where(p => EF.Functions.Like(p.Name, pattern))
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync(ct);
  }

  // Sin rastreo: la compra descuenta con TryDecrementStockAsync y nadie modifica esta
  // instancia.
  public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(sku)) return null;

    var normalized = sku.Trim().ToLower();

    return await Query()
        .Include(p => p.Category)
        .FirstOrDefaultAsync(p => p.SKU.ToLower().Trim() == normalized, ct);
  }

  public async Task<bool> TryDecrementStockAsync(
      int productId, int quantity, CancellationToken ct = default)
  {
    // Un solo UPDATE con la condición `Stock >= quantity` dentro de la sentencia: entre
    // comprobar y descontar no cabe otra compra. Devuelve las filas afectadas, 0 si no
    // se cumplió la condición.

    // El instante se captura fuera del árbol de expresión: dentro, EF lo traduciría a
    // GETDATE() y estamparía con el reloj de SQL Server en vez del reloj del proceso.
    var now = DateTime.Now;

    var affected = await _db.Products
        .Where(p => p.Id == productId && p.Stock >= quantity)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(p => p.Stock, p => p.Stock - quantity)
            // ExecuteUpdate no pasa por SaveChangesAsync, así que la auditoría automática
            // no se dispara y UpdatedAt se estampa aquí a mano.
            .SetProperty(p => p.UpdatedAt, _ => now), ct);

    return affected == 1;
  }

  public async Task<bool> SkuExistsAsync(string sku, int? excludeId = null, CancellationToken ct = default)
  {
    var normalized = sku.Trim().ToLower();

    var query = _db.Products
        .Where(p => p.SKU.ToLower().Trim() == normalized);

    if (excludeId is int id)
      query = query.Where(p => p.Id != id);

    return await query.AnyAsync(ct);
  }


  // // Versión anterior: devolvía un bool que no distinguía 404 de 409.
  // public async Task<bool> BuyProduct(string sku, int quantity)
  // {
  //   if (string.IsNullOrWhiteSpace(sku) || quantity <= 0) return false;
  //
  //   var normalizedSku = sku.ToLower().Trim();
  //   var product = await _db.Products.FirstOrDefaultAsync(
  //       p => p.SKU.ToLower().Trim() == normalizedSku);
  //
  //   if (product is null || product.Stock < quantity) return false;
  //
  //   product.Stock -= quantity;
  //   product.UpdatedAt = DateTime.Now;
  //
  //   _db.Products.Update(product);
  //   await _db.SaveChangesAsync();
  //
  //   return true;
  // }
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
