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

  /// <summary>Elementos a saltar en la consulta.</summary>
  public int Skip => SkipFor(Page, PageSize);

  /// <summary>
  /// Los elementos a saltar de una página, saturando en vez de desbordar.
  /// </summary>
  /// <remarks>
  /// En <c>int</c>, <c>(page - 1) * pageSize</c> desborda a negativo con `?page=2147483647`
  /// y SQL Server rechaza un OFFSET negativo: un 500 desde el query string. Todo el que
  /// pagine debe usar esto en vez de repetir la fórmula.
  /// </remarks>
  public static int SkipFor(int page, int pageSize)
      => (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
}
