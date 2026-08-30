using ApiEcommerce.Data;
using ApiEcommerce.Models;
using ApiEcommerce.Shared.Paging;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Repository;


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
        .ThenByDescending(p => p.Id);   // desempate: dos productos creados en el mismo
                                        // tick harían el orden no determinista y una
                                        // fila podría repetirse entre páginas.

    // El COUNT va sobre la MISMA consulta base que la página (EF elimina el Include y
    // el OrderBy al traducirlo). Contar sobre _db.Products a secas funciona solo
    // mientras no haya filtros; en cuanto se añada un Where, TotalItems mentiría.
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

  // Sin rastreo: desde que la compra usa TryDecrementStockAsync, nadie modifica
  // esta instancia. Rastrearla solo costaba memoria y un snapshot inútil.
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
    // ExecuteUpdateAsync emite UN solo UPDATE con su WHERE, sin cargar la entidad
    // ni pasar por el change tracker. La condición `Stock >= quantity` se evalúa
    // DENTRO de la sentencia, así que entre comprobar y descontar no cabe nadie:
    // si dos peticiones llegan a la vez, la base serializa los dos UPDATE sobre la
    // misma fila y la segunda ve el stock ya descontado.
    //
    // Devuelve el número de filas afectadas: 0 = la condición no se cumplió.
    // El instante se captura FUERA del árbol de expresión. Si se escribe
    // `_ => DateTime.Now` dentro, EF no lo evalúa en cliente: lo traduce a `GETDATE()`,
    // o sea el reloj del SERVIDOR SQL. El resto del proyecto estampa con el reloj del
    // PROCESO (AppDbContext.StampAuditFields), así que en contenedores con zonas
    // horarias distintas un producto comprado y el mismo producto editado por PATCH
    // acababan con marcas de tiempo desfasadas horas.
    var now = DateTime.Now;

    var affected = await _db.Products
        .Where(p => p.Id == productId && p.Stock >= quantity)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(p => p.Stock, p => p.Stock - quantity)
            // OJO: ExecuteUpdate NO pasa por SaveChangesAsync, así que la auditoría
            // automática de AppDbContext no se dispara. UpdatedAt hay que ponerlo
            // aquí a mano; si no, la fila cambia y la marca de tiempo se queda vieja.
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


  // // Versión anterior. Aplicaba la regla de negocio (stock suficiente) dentro del
  // // repositorio y devolvía un bool que no distinguía 404 de 409. Hoy esa decisión
  // // vive en ProductService.BuyAsync y el UpdatedAt lo estampa AppDbContext.
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
