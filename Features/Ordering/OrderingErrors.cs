using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Ordering.Ports;

namespace ApiEcommerce.Features.Ordering;


/// <summary>Los errores de una línea al comprar, con <c>code</c> estable y el <c>sku</c>.</summary>
/// <remarks>
/// Los mismos códigos que el catálogo usa en <c>POST /product/buy</c>, pero escritos aquí:
/// Ordering no referencia tipos de Catalog (§5.2), y el contrato es el JSON, no la clase.
/// Antes era un único 409 genérico para no hacer consultable el inventario; la cotización
/// anónima ya dice cuánto queda, así que esconderlo aquí solo le quitaba información al front.
/// </remarks>
public static class OrderingErrors
{
  public static CustomAppException ForLine(TakeFailure failure, string sku, int quantity)
  {
    var exception = failure switch
    {
      TakeFailure.Unavailable => new CustomAppException(
          "sku_unavailable", $"'{sku}' is no longer available.", HttpStatusCode.Conflict),
      TakeFailure.InsufficientStock => new CustomAppException(
          "insufficient_stock", $"Insufficient stock for '{sku}': requested {quantity}.", HttpStatusCode.Conflict),
      _ => new CustomAppException(
          "sku_not_found", $"'{sku}' does not exist.", HttpStatusCode.Conflict)
    };

    exception.Extensions["sku"] = sku;
    return exception;
  }
}
