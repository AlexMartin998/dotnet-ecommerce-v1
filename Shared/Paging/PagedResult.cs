namespace ApiEcommerce.Shared.Paging;


/// <summary>
/// Una página de resultados con los metadatos que el cliente necesita para pintar un
/// paginador.
/// </summary>
/// <remarks>
/// Lleva <see cref="TotalItems"/> y no solo <see cref="TotalPages"/> porque el total es lo
/// que hace falta para el «mostrando 1-10 de 137», y calcularlo para tirarlo es pagar el
/// <c>COUNT(*)</c> sin cobrarlo. Es un <c>record</c>: valor inmutable de transporte.
/// </remarks>
/// <typeparam name="T">Tipo de los elementos, siempre un DTO.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems)
{
  /// <summary>Número total de páginas. Con 0 elementos es 0, no 1.</summary>
  public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalItems / (double)PageSize);

  /// <summary>¿Hay página anterior?</summary>
  public bool HasPrevious => Page > 1;

  /// <summary>¿Hay página siguiente?</summary>
  public bool HasNext => Page < TotalPages;

  /// <summary>Página vacía, para cuando la consulta no devuelve nada.</summary>
  /// <remarks>
  /// Una página fuera de rango devuelve 200 con lista vacía, no 404: la colección existe;
  /// lo que no hay son resultados.
  /// </remarks>
  public static PagedResult<T> Empty(int page, int pageSize, int totalItems = 0)
      => new([], page, pageSize, totalItems);
}
