using System.Net;
using ApiEcommerce.Exceptions;

namespace ApiEcommerce.Features.Catalog;


/// <summary>Los errores del catálogo cuyo <c>code</c> es contrato con el front.</summary>
/// <remarks>
/// Fábricas y no una clase por error: son <see cref="CustomAppException"/> con un código
/// estable, y tenerlos en un sitio evita que dos servicios escriban el mismo código distinto.
/// </remarks>
public static class CatalogErrors
{
  /// <summary>La talla ya existe en el producto. 409.</summary>
  public static CustomAppException DuplicateSize(string size) =>
      new("duplicate_size", $"The product already has the size '{size}'.", HttpStatusCode.Conflict);

  /// <summary>Una variante sin talla activa y tallas activas no conviven. 409.</summary>
  public static CustomAppException VariantKindMismatch() =>
      new("variant_kind_mismatch",
          "A product is sold either without sizes (a single variant with no size) or with sizes, " +
          "not both. Deactivate the other kind of variant first.",
          HttpStatusCode.Conflict);

  /// <summary>La talla existe pero está desactivada. 409, con el <c>sku</c>.</summary>
  public static CustomAppException SkuUnavailable(string sku) =>
      WithSku(new("sku_unavailable", $"'{sku}' is no longer available.", HttpStatusCode.Conflict), sku);

  /// <summary>No hay tantas unidades de esa talla. 409, con el <c>sku</c>.</summary>
  public static CustomAppException InsufficientStock(string sku, int requested) =>
      WithSku(new("insufficient_stock",
          $"Insufficient stock for '{sku}': requested {requested}.", HttpStatusCode.Conflict), sku);

  private static CustomAppException WithSku(CustomAppException exception, string sku)
  {
    exception.Extensions["sku"] = sku;
    return exception;
  }
}
