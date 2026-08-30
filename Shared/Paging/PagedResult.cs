namespace ApiEcommerce.Shared.Paging;


/// <summary>
/// Una página de resultados con los metadatos que el cliente necesita para pintar
/// un paginador.
/// </summary>
/// <remarks>
/// <para>
/// Lleva <see cref="TotalItems"/> y no solo <see cref="TotalPages"/>: el total es el
/// dato que hace falta para el "mostrando 1-10 de 137", y calcularlo para tirarlo
/// (como hace el código de referencia) es pagar el <c>COUNT(*)</c> sin cobrarlo.
/// </para>
/// <para>
/// Es un <c>record</c>: es un valor inmutable de transporte, sin identidad ni
/// comportamiento.
/// </para>
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

  public bool HasPrevious => Page > 1;

  public bool HasNext => Page < TotalPages;

  /// <summary>Página vacía, para cuando la consulta no devuelve nada.</summary>
  /// <remarks>
  /// Una página fuera de rango devuelve <b>200 con lista vacía</b>, no 404: el recurso
  /// (la colección) existe; lo que no hay son resultados. El código de referencia
  /// devolvía 404 cuando la tabla estaba vacía, que es semánticamente falso.
  /// </remarks>
  public static PagedResult<T> Empty(int page, int pageSize, int totalItems = 0)
      => new([], page, pageSize, totalItems);
}
