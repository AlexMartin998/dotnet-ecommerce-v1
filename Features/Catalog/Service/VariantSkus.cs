using System.Text;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>El SKU por defecto de una talla: <c>{SKU del producto}-{talla}</c>.</summary>
public static class VariantSkus
{
  /// <summary>
  /// Une el SKU del producto con la talla normalizada a mayúsculas, con guiones por
  /// separador: <c>("TSH-01", "One size")</c> da <c>TSH-01-ONE-SIZE</c>.
  /// </summary>
  /// <remarks>
  /// Se normaliza porque el SKU solo admite letras, dígitos y guiones, y una talla
  /// (<c>"1/2"</c>, <c>"One size"</c>) no. Puede pasar de 50 caracteres: eso lo rechaza la
  /// regla, no este helper.
  /// </remarks>
  public static string For(string productSku, string size)
  {
    var builder = new StringBuilder(productSku.Trim());

    builder.Append('-');

    foreach (var c in size.Trim().ToUpperInvariant())
    {
      if (char.IsAsciiLetterOrDigit(c)) builder.Append(c);
      else if (builder[^1] != '-') builder.Append('-');
    }

    return builder.ToString().TrimEnd('-');
  }
}
