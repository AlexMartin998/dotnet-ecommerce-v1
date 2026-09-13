using ApiEcommerce.Data;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Shared.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Features.Catalog.Repository;


/// <inheritdoc cref="IProductVariantRepository"/>
public class ProductVariantRepository(AppDbContext db)
    : BaseRepository<ProductVariant>(db), IProductVariantRepository
{
  public async Task<ProductVariant?> GetBySkuAsync(string sku, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(sku)) return null;

    var normalized = sku.Trim();

    return await Query()
        .Include(v => v.Product)
        .FirstOrDefaultAsync(v => v.SKU == normalized, ct);   // usa IX_ProductVariants_SKU
  }

  public async Task<IReadOnlyList<ProductVariant>> GetForProductAsync(
      int productId, CancellationToken ct = default)
      => await Query()
          .Where(v => v.ProductId == productId)
          // Position no es única: el Id desempata para que el orden sea estable.
          .OrderBy(v => v.Position)
          .ThenBy(v => v.Id)
          .ToListAsync(ct);

  public async Task<ProductVariant?> GetTrackedAsync(
      int productId, int variantId, CancellationToken ct = default)
      => await Query(tracking: true)
          .FirstOrDefaultAsync(v => v.Id == variantId && v.ProductId == productId, ct);

  public async Task<bool> SkuExistsAsync(string sku, int? excludeProductId = null, CancellationToken ct = default)
  {
    var normalized = sku.Trim();

    // Mira lo mismo que el índice único filtrado: las variantes no retiradas.
    var query = _dbSet.IgnoreQueryFilters().Where(v => v.SKU == normalized && v.DeletedAt == null);

    if (excludeProductId is int productId)
      query = query.Where(v => v.ProductId != productId);

    return await query.AnyAsync(ct);
  }

  public async Task LockProductAsync(int productId, CancellationToken ct = default)
  {
    var resource = $"catalog:product-variants:{productId}";

    // THROW si no se obtiene: seguir sin el lock sería volver a la carrera en silencio.
    await _db.Database.ExecuteSqlInterpolatedAsync($"""
        DECLARE @result int;
        EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive',
                                     @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @result < 0 THROW 51000, 'Could not lock the product variants.', 1;
        """, ct);
  }

  public async Task SyncUnsizedSkuAsync(int productId, string sku, CancellationToken ct = default)
  {
    var now = DateTime.Now;
    var normalized = sku.Trim();

    await _dbSet
        .Where(v => v.ProductId == productId && v.Size == null)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(v => v.SKU, normalized)
            .SetProperty(v => v.UpdatedAt, now), ct);
  }

  public async Task<bool> TryDecrementStockAsync(
      int variantId, int quantity, CancellationToken ct = default)
  {
    // Fuera del árbol de expresión: dentro, DateTime.Now se traduciría a GETDATE().
    var now = DateTime.Now;

    // Sin el filtro global y con la COPIA de DeletedAt: así la sentencia solo toca
    // ProductVariants. Con el filtro hacía JOIN a Products, tomaba sus locks en el orden
    // contrario al del borrado lógico, y la revisión vio deadlocks resueltos por reintento.
    var affected = await _dbSet
        .IgnoreQueryFilters()
        .Where(v => v.Id == variantId && v.DeletedAt == null && v.IsActive && v.Stock >= quantity)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(v => v.Stock, v => v.Stock - quantity)
            // ExecuteUpdate no pasa por SaveChangesAsync: la auditoría va a mano.
            .SetProperty(v => v.UpdatedAt, _ => now), ct);

    return affected == 1;
  }

  public async Task IncrementStockAsync(int variantId, int quantity, CancellationToken ct = default)
  {
    var now = DateTime.Now;

    // CON IgnoreQueryFilters, al revés que al descontar: devolver no vende nada, y perder el
    // stock de un producto retirado impediría recuperarlo si vuelve al catálogo.
    await _dbSet
        .IgnoreQueryFilters()
        .Where(v => v.Id == variantId)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(v => v.Stock, v => v.Stock + quantity)
            .SetProperty(v => v.UpdatedAt, _ => now), ct);
  }

  public async Task<bool> IncrementStockBySkuAsync(string sku, int quantity, CancellationToken ct = default)
  {
    var now = DateTime.Now;
    var normalized = sku.Trim();

    // Sin retiradas: tras reutilizar un SKU puede haber dos filas con él, y la línea vieja
    // sin VariantId no dice cuál era. Se devuelve a la que está viva.
    var affected = await _dbSet
        .IgnoreQueryFilters()
        .Where(v => v.SKU == normalized && v.DeletedAt == null)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(v => v.Stock, v => v.Stock + quantity)
            .SetProperty(v => v.UpdatedAt, _ => now), ct);

    return affected == 1;
  }
}
