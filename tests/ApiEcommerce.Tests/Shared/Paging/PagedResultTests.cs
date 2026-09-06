using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Tests.Shared.Paging;


/// <summary>
/// Metadatos de paginación. Son aritmética pura, y justo por eso se rompen en los
/// BORDES: la página 0, la lista vacía, la última página exacta.
/// </summary>
public class PagedResultTests
{
  [Theory]
  [InlineData(0, 10, 0)]    // sin resultados son 0 páginas, NO 1: un paginador con "página 1 de 1" sobre una lista vacía miente
  [InlineData(1, 10, 1)]
  [InlineData(10, 10, 1)]   // borde exacto: 10 de 10 es UNA página, no dos
  [InlineData(11, 10, 2)]
  [InlineData(137, 10, 14)]
  public void TotalPages_RoundsUp(int totalItems, int pageSize, int expected)
      => Assert.Equal(expected, new PagedResult<string>([], 1, pageSize, totalItems).TotalPages);

  [Fact]
  public void TotalPages_WithPageSizeZero_IsZeroAndDoesNotDivideByZero()
  {
    // PageQuery lo impide con [Range], pero el record es público y no se defiende solo
    // en ningún otro sitio: una división entera por 0 aquí sería un 500.
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
    // Una página fuera de rango devuelve 200 con lista vacía, no 404: el recurso
    // (la colección) existe; lo que no hay son resultados en ESA página. Y el total
    // real tiene que seguir viajando para que el cliente sepa a dónde volver.
    var empty = PagedResult<string>.Empty(page: 99, pageSize: 10, totalItems: 137);

    Assert.Empty(empty.Items);
    Assert.Equal(99, empty.Page);
    Assert.Equal(137, empty.TotalItems);
    Assert.True(empty.HasPrevious);
  }
}
