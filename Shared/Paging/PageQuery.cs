using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Paging;


/// <summary>Parámetros de paginación de un listado (<c>?page=1&amp;pageSize=10</c>).</summary>
/// <remarks>
/// El tope de <see cref="PageSize"/> no es decorativo: sin él, un
/// <c>?pageSize=1000000</c> en un endpoint anónimo es una denegación de servicio de
/// una sola petición.
/// </remarks>
public class PageQuery
{
  public const int MaxPageSize = 100;

  [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater")]
  public int Page { get; set; } = 1;

  [Range(1, MaxPageSize, ErrorMessage = "pageSize must be between 1 and 100")]
  public int PageSize { get; set; } = 10;

  /// <summary>Elementos a saltar. Se calcula aquí para no repetir la fórmula en cada repositorio.</summary>
  /// <remarks>
  /// ⚠️ El cálculo se hace en <c>long</c> y se satura. <c>(Page - 1) * PageSize</c> en
  /// <c>int</c> **desborda** con `?page=2147483647`, da un número negativo, y SQL Server
  /// responde «The offset specified in a OFFSET clause may not be negative»: un **500** con
  /// traza a partir de un query string. Saturando, la página fuera de rango devuelve
  /// **200 con `[]`**, que es la regla que ya fija <c>PagedResult.Empty</c> — el recurso
  /// existe, lo que no hay son resultados.
  /// </remarks>
  public int Skip => (int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue);
}
