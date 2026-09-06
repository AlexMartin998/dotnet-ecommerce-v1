using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Paging;


/// <summary>Parámetros de paginación de un listado (<c>?page=1&amp;pageSize=10</c>).</summary>
/// <remarks>
/// El tope de <see cref="PageSize"/> no es decorativo: sin él, un <c>?pageSize=1000000</c>
/// en un endpoint anónimo es una denegación de servicio de una sola petición.
/// </remarks>
public class PageQuery
{
  /// <summary>Tope de <see cref="PageSize"/>.</summary>
  public const int MaxPageSize = 100;

  /// <summary>Página pedida, base 1.</summary>
  [Range(1, int.MaxValue, ErrorMessage = "page must be 1 or greater")]
  public int Page { get; set; } = 1;

  /// <summary>Elementos por página.</summary>
  [Range(1, MaxPageSize, ErrorMessage = "pageSize must be between 1 and 100")]
  public int PageSize { get; set; } = 10;

  /// <summary>Elementos a saltar. Se calcula aquí para no repetir la fórmula en cada repositorio.</summary>
  /// <remarks>
  /// El cálculo va en <c>long</c> y se satura: en <c>int</c> desborda con
  /// <c>?page=2147483647</c>, da negativo y SQL Server responde con un 500. Saturando, la
  /// página fuera de rango devuelve 200 con <c>[]</c>, como fija <c>PagedResult.Empty</c>.
  /// </remarks>
  public int Skip => (int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue);
}
