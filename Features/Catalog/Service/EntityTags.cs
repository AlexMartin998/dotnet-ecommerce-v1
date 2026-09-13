using ApiEcommerce.Exceptions;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>La comprobación de <c>If-Match</c> contra un <c>rowversion</c>, para producto y variante.</summary>
public static class EntityTags
{
  /// <summary>
  /// Compara las versiones del <c>If-Match</c> con la de la base y cierra el lost update
  /// entre dos escrituras.
  /// </summary>
  /// <remarks>
  /// Es opcional: sin <c>If-Match</c> no se comprueba nada. La ventana entre esta comparación
  /// y el UPDATE la cubre el <c>[Timestamp]</c> de la entidad.
  /// </remarks>
  /// <exception cref="BadOperationAppException">Una etiqueta no es base64.</exception>
  /// <exception cref="PreconditionFailedAppException">Ninguna etiqueta casa.</exception>
  public static void EnsureMatches(IReadOnlyList<string>? clientVersions, byte[]? current)
  {
    if (clientVersions is null || clientVersions.Count == 0) return;

    var expected = new List<byte[]>(clientVersions.Count);

    foreach (var version in clientVersions)
    {
      try
      {
        expected.Add(Convert.FromBase64String(version));
      }
      catch (FormatException)
      {
        // 400 y no 412: no es que la precondición falle, es que ni siquiera es un token.
        throw new BadOperationAppException("The If-Match header is not a valid entity tag.");
      }
    }

    // El RFC 9110 dice que basta con que UNA case.
    if (current is not null && expected.Any(e => e.SequenceEqual(current)))
      return;

    throw new PreconditionFailedAppException();
  }
}
