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
        .Include(p => p.Images)
        .Include(p => p.Variants)
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync(ct);
  }

  public async Task<Product?> GetByIdWithCategoryAsync(int id, CancellationToken ct = default)
  {
    return await Query()
        .Include(p => p.Category)
        .Include(p => p.Images)
        .Include(p => p.Variants)
        .FirstOrDefaultAsync(p => p.Id == id, ct);
  }

  public async Task<PagedResult<Product>> GetPagedWithCategoryAsync(
      int page, int pageSize, CancellationToken ct = default)
  {
    var ordered = Query()
        .Include(p => p.Category)
        .Include(p => p.Images)
        .Include(p => p.Variants)
        .OrderByDescending(p => p.CreatedAt)
        .ThenByDescending(p => p.Id);   // desempate: sin él, dos productos creados en el
                                        // mismo tick pueden repetirse entre páginas.

    // El COUNT va sobre la misma consulta base que la página, para que TotalItems siga
    // siendo cierto el día que se añada un Where.
    var total = await ordered.CountAsync(ct);

    var items = await ordered
        .Skip(PageQuery.SkipFor(page, pageSize))
        .Take(pageSize)
        .ToListAsync(ct);

    return new PagedResult<Product>(items, page, pageSize, total);
  }

  public async Task<ICollection<Product>> GetProductsForCategoryAsync(int categoryId, CancellationToken ct = default)
  {
    return await Query()
        .Include(p => p.Category)
        .Include(p => p.Images)
        .Include(p => p.Variants)
        .Where(p => p.CategoryId == categoryId)
        .OrderByDescending(p => p.CreatedAt)
        .ThenByDescending(p => p.Id)   // CreatedAt no es único: sin desempate el orden no es estable
        .ToListAsync(ct);
  }

  public async Task<ICollection<Product>> SearchProductAsync(string name, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(name)) return [];

    var pattern = $"%{name.Trim()}%";

    return await Query()
        .Include(p => p.Category)
        .Include(p => p.Images)
        .Include(p => p.Variants)
        .Where(p => EF.Functions.Like(p.Name, pattern))
        .OrderByDescending(p => p.CreatedAt)
        .ToListAsync(ct);
  }

  // Sin rastreo: la compra descuenta con TryDecrementStockAsync y nadie modifica esta
  // instancia.
  public async Task<Product?> GetBySkuAsync(string sku, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(sku)) return null;

    var normalized = sku.Trim();

    return await Query()
        .Include(p => p.Category)
        .Include(p => p.Images)
        .Include(p => p.Variants)
        .FirstOrDefaultAsync(p => p.SKU == normalized, ct);   // usa IX_Products_SKU
  }

  public async Task<Product?> GetByIdWithImagesAsync(int id, CancellationToken ct = default)
      => await Query(tracking: true)
          .Include(p => p.Images)
          .Include(p => p.Variants)
          .FirstOrDefaultAsync(p => p.Id == id, ct);

  public async Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default)
      => await Query().AnyAsync(p => p.Slug == slug, ct);

  public async Task<Product?> GetBySlugAsync(string slug, CancellationToken ct = default)
      => await Query()
          .Include(p => p.Category)
          .Include(p => p.Images)
        .Include(p => p.Variants)
          .FirstOrDefaultAsync(p => p.Slug == slug, ct);

  public async Task<bool> SoftDeleteAsync(int id, CancellationToken ct = default)
  {
    // Fuera del árbol de expresión: dentro, DateTime.Now se traduciría a GETDATE().
    var now = DateTime.Now;

    // Condicional: dos administradores borrando a la vez no pueden contarlo dos veces.
    // El filtro global ya excluye los retirados, así que `DeletedAt == null` es implícito.
    var affected = await Query(tracking: true)
        .Where(p => p.Id == id)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(p => p.DeletedAt, now)
            .SetProperty(p => p.UpdatedAt, now), ct);

    if (affected != 1) return false;

    // La copia en las variantes, que es lo que libera sus SKU en el índice filtrado y lo que
    // mira la compra. Quien llama abre la transacción: sin ella, un fallo aquí dejaría el
    // producto retirado con sus tallas todavía a la venta.
    await _db.ProductVariants
        .IgnoreQueryFilters()
        .Where(v => v.ProductId == id && v.DeletedAt == null)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(v => v.DeletedAt, now)
            .SetProperty(v => v.UpdatedAt, now), ct);

    return true;
  }

  public async Task<(int Total, int OutOfStock, int LowStock)> CountStockAsync(
      int lowStockThreshold, CancellationToken ct = default)
  {
    // Una sola ida a la base: tres COUNT condicionales en la misma agregación. El stock de
    // cada producto es la suma de sus tallas activas (planning/27), calculada en SQL.
    var counts = await Query()
        // En long: la suma de varias tallas puede pasar de int y SQL Server daría 8115.
        .Select(p => new { Stock = p.Variants.Where(v => v.IsActive).Sum(v => (long?)v.Stock) ?? 0L })
        .GroupBy(_ => 1)
        .Select(g => new
        {
          Total = g.Count(),
          OutOfStock = g.Count(p => p.Stock <= 0),
          LowStock = g.Count(p => p.Stock > 0 && p.Stock <= lowStockThreshold)
        })
        .FirstOrDefaultAsync(ct);

    // Catálogo vacío: el GROUP BY no devuelve ninguna fila.
    return counts is null ? (0, 0, 0) : (counts.Total, counts.OutOfStock, counts.LowStock);
  }

  // // Sustituidos por ProductVariantRepository (planning/27): el stock vive en la talla.
  // public async Task<bool> TryDecrementStockAsync(
  //     int productId, int quantity, CancellationToken ct = default)
  // {
  //   // Un solo UPDATE con la condición `Stock >= quantity` dentro de la sentencia: entre
  //   // comprobar y descontar no cabe otra compra. Devuelve las filas afectadas, 0 si no
  //   // se cumplió la condición.
  //
  //   // El instante se captura fuera del árbol de expresión: dentro, EF lo traduciría a
  //   // GETDATE() y estamparía con el reloj de SQL Server en vez del reloj del proceso.
  //   var now = DateTime.Now;
  //
  //   var affected = await _db.Products
  //       .Where(p => p.Id == productId && p.Stock >= quantity)
  //       .ExecuteUpdateAsync(setters => setters
  //           .SetProperty(p => p.Stock, p => p.Stock - quantity)
  //           // ExecuteUpdate no pasa por SaveChangesAsync, así que la auditoría automática
  //           // no se dispara y UpdatedAt se estampa aquí a mano.
  //           .SetProperty(p => p.UpdatedAt, _ => now), ct);
  //
  //   return affected == 1;
  // }
  //
  // public async Task IncrementStockAsync(
  //     int productId, int quantity, CancellationToken ct = default)
  // {
  //   var now = DateTime.Now;
  //
  //   await _db.Products
  //       .Where(p => p.Id == productId)
  //       .ExecuteUpdateAsync(setters => setters
  //           .SetProperty(p => p.Stock, p => p.Stock + quantity)
  //           .SetProperty(p => p.UpdatedAt, _ => now), ct);
  // }

  public async Task<bool> SkuExistsAsync(string sku, int? excludeId = null, CancellationToken ct = default)
  {
    var normalized = sku.Trim();

    var query = _db.Products
        .Where(p => p.SKU == normalized);   // sin funciones sobre la columna: usa IX_Products_SKU

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
