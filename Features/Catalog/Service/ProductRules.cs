using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Reglas de negocio de <see cref="Product"/>: SKU único y categoría existente.
/// </summary>
/// <remarks>
/// Validar la FK aquí es obligatorio: si se deja pasar un <c>CategoryId</c>
/// inexistente, EF revienta con un error de clave foránea y el cliente recibe un
/// 500 en vez del 400 que corresponde.
/// </remarks>
public sealed class ProductRules(
    IProductRepository productRepository,
    ICategoryRepository categoryRepository)
  : IEntityRules<Product, CreateProductDto, UpdateProductDto>
{
  public string EntityName => "Product";

  public async Task EnsureCanCreateAsync(CreateProductDto dto, CancellationToken ct = default)
  {
    await EnsureCategoryExistsAsync(dto.CategoryId, ct);
    await EnsureSkuIsFreeAsync(dto.SKU, excludeId: null, ct);
  }

  public async Task EnsureCanUpdateAsync(
      int id, UpdateProductDto dto, Product existing, CancellationToken ct = default)
  {
    EnsureVersionMatches(dto.RowVersion, existing);

    // PATCH: solo se valida lo que el cliente envía.
    if (dto.CategoryId is int categoryId)
      await EnsureCategoryExistsAsync(categoryId, ct);

    if (!string.IsNullOrWhiteSpace(dto.SKU))
      await EnsureSkuIsFreeAsync(dto.SKU, excludeId: id, ct);
  }

  // ---- helpers privados ---------------------------------------------------

  /// <summary>
  /// Cierra el <i>lost update</i> entre dos administradores.
  /// </summary>
  /// <remarks>
  /// <para>
  /// El escenario: A lee el producto, B lo edita, A guarda. Sin esto A pisa el cambio de B
  /// <b>sin que nadie se entere</b>, y no lo salva que <c>Product.RowVersion</c> exista:
  /// el PATCH relee la fila, así que EF compara contra el rowversion que acaba de leer —el
  /// de B— y todo cuadra. Lo único que rompe el empate es el token que A leyó en SU GET,
  /// y ese solo puede llegar del cliente.
  /// </para>
  /// <para>
  /// <b>Es opcional a propósito.</b> Sin <c>If-Match</c> el PATCH funciona como siempre:
  /// exigirlo rompería a todos los clientes actuales, y la protección la pide quien sabe
  /// que está editando algo que leyó antes.
  /// </para>
  /// <para>
  /// La ventana entre esta comprobación y el UPDATE la cubre EF: <c>[Timestamp]</c> mete
  /// el rowversion en el <c>WHERE</c>, y si cambia entremedias sale
  /// <c>DbUpdateConcurrencyException</c> → 409. Dos redes, cada una para su carrera.
  /// </para>
  /// </remarks>
  private static void EnsureVersionMatches(string? clientVersion, Product existing)
  {
    if (string.IsNullOrWhiteSpace(clientVersion)) return;

    byte[] expected;

    try
    {
      expected = Convert.FromBase64String(clientVersion);
    }
    catch (FormatException)
    {
      // 400 y no 412: no es que la precondición falle, es que ni siquiera es un token.
      throw new BadOperationAppException("The If-Match header is not a valid entity tag.");
    }

    if (existing.RowVersion is null || !expected.SequenceEqual(existing.RowVersion))
      throw new PreconditionFailedAppException();
  }

  private async Task EnsureCategoryExistsAsync(int categoryId, CancellationToken ct)
  {
    // 400 y no 404: el recurso pedido es el producto; la categoría inexistente
    // hace que el request sea inválido en sí mismo.
    if (!await categoryRepository.ExistsAsync(categoryId, ct))
      throw new BadOperationAppException($"Category with id {categoryId} does not exist.");
  }

  private async Task EnsureSkuIsFreeAsync(string sku, int? excludeId, CancellationToken ct)
  {
    if (await productRepository.SkuExistsAsync(sku, excludeId, ct))
      throw new ConflictAppException($"SKU '{sku}' is already registered.");
  }
}
