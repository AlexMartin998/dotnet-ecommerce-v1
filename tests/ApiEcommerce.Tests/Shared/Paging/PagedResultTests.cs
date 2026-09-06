using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Tests.Shared.Paging;


/// <summary>
/// Metadatos de paginación: aritmética pura, que se rompe en los bordes —página 0, lista
/// vacía, última página exacta—.
/// </summary>
public class PagedResultTests
{
  [Theory]
  [InlineData(0, 10, 0)]    // sin resultados son 0 páginas: "página 1 de 1" sobre una lista vacía miente
  [InlineData(1, 10, 1)]
  [InlineData(10, 10, 1)]   // borde exacto: 10 de 10 es una página, no dos
  [InlineData(11, 10, 2)]
  [InlineData(137, 10, 14)]
  public void TotalPages_RoundsUp(int totalItems, int pageSize, int expected)
      => Assert.Equal(expected, new PagedResult<string>([], 1, pageSize, totalItems).TotalPages);

  [Fact]
  public void TotalPages_WithPageSizeZero_IsZeroAndDoesNotDivideByZero()
  {
    // PageQuery lo impide con [Range], pero el record es público: una división entera
    // por 0 aquí sería un 500.
    Assert.Equal(0, new PagedResult<string>([], 1, 0, 137).TotalPages);
  }

  [Theory]
  [InlineData(1, 137, false, true)]    // primera
  [InlineData(2, 137, true, true)]     // intermedia
  [InlineData(14, 137, true, false)]   // última
  [InlineData(99, 137, true, false)]   // fuera de rango: no hay siguiente, y aun así es 200 con []
  [InlineData(1, 0, false, false)]     // vacía
  public void HasPreviousAndHasNext_AtTheEdges(
      int page, int totalItems, bool hasPrevious, bool hasNext)
  {
    var result = new PagedResult<string>([], page, 10, totalItems);

    Assert.Equal(hasPrevious, result.HasPrevious);
    Assert.Equal(hasNext, result.HasNext);
  }

  [Fact]
  public void Empty_KeepsTheRequestedPageAndTheRealTotal()
  {
    // Fuera de rango son 200 y lista vacía, no 404: la colección existe, y el total real
    // sigue viajando para que el cliente sepa a dónde volver.
    var empty = PagedResult<string>.Empty(page: 99, pageSize: 10, totalItems: 137);

    Assert.Empty(empty.Items);
    Assert.Equal(99, empty.Page);
    Assert.Equal(137, empty.TotalItems);
    Assert.True(empty.HasPrevious);
  }

  // ---- PageQuery -----------------------------------------------------------

  [Theory]
  [InlineData(1, 10, 0)]
  [InlineData(3, 10, 20)]
  [InlineData(int.MaxValue, 100, int.MaxValue)]
  [InlineData(int.MaxValue, 1, int.MaxValue - 1)]
  public void Skip_NeverOverflowsIntoANegativeOffset(int page, int pageSize, int expected)
  {
    // `(Page - 1) * PageSize` desborda en `int` con `?page=2147483647`: el offset sale
    // negativo y SQL Server responde con un 500, en todos los endpoints paginados.
    var query = new PageQuery { Page = page, PageSize = pageSize };

    Assert.Equal(expected, query.Skip);
    Assert.True(query.Skip >= 0);
  }
}
