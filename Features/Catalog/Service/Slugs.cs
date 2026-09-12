using System.Globalization;
using System.Text;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>Convierte un nombre en un identificador apto para una URL.</summary>
public static class Slugs
{
  /// <summary>Largo máximo, alineado con <c>Product.Slug</c>.</summary>
  private const int MaxLength = 200;

  /// <summary>
  /// Normaliza a minúsculas sin acentos, con guiones por separador. Devuelve
  /// <c>null</c> si no queda nada utilizable.
  /// </summary>
  /// <remarks>
  /// Se descomponen los acentos y se descartan las marcas diacríticas, o "Camión" y "Camion"
  /// darían slugs distintos y el índice único no los vería como el mismo producto.
  /// </remarks>
  public static string? From(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return null;

    var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);

    var builder = new StringBuilder(decomposed.Length);

    foreach (var c in decomposed)
    {
      if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

      if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
      // Un solo guion por tanda de separadores: "a  -  b" no puede dar "a---b".
      else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
    }

    var slug = builder.ToString().Trim('-');

    if (slug.Length > MaxLength) slug = slug[..MaxLength].Trim('-');

    return slug.Length == 0 ? null : slug;
  }
}
