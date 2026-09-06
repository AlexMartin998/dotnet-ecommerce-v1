using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Documents;


/// <summary>
/// Almacén de documentos sobre el sistema de ficheros. La implementación de hoy.
/// </summary>
/// <remarks>
/// Escribe fuera de <c>wwwroot/</c> y se sirve por un endpoint que comprueba de quién es
/// la orden. La clave tiene forma <c>aaaa/mm/&lt;32 hex&gt;.pdf</c>: las carpetas por año y
/// mes evitan un único directorio con cientos de miles de ficheros.
/// </remarks>
public sealed class LocalDocumentStore : IDocumentStore
{
  private readonly ILogger<LocalDocumentStore> _logger;

  /// <summary>Raíz del almacén, ya absoluta.</summary>
  /// <remarks>
  /// Se resuelve una sola vez y contra el content root: <c>Path.GetFullPath</c> usa el
  /// <c>cwd</c>, así que arrancar desde otra carpeta cambiaría dónde acaban los
  /// comprobantes y todas las claves guardadas darían 404.
  /// </remarks>
  private readonly string _root;

  /// <summary>Resuelve y valida la raíz del almacén.</summary>
  public LocalDocumentStore(
      IOptions<DocumentStorageOptions> options,
      IWebHostEnvironment environment,
      ILogger<LocalDocumentStore> logger)
  {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(environment);

    _logger = logger;

    var configured = options.Value.RootPath;

    // `TrimEndingDirectorySeparator` porque `GetFullPath` conserva la barra final y la
    // comprobación de abajo concatena una: con "/" al final ninguna clave pasaría el filtro.
    _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(configured)
        ? configured
        : Path.Combine(environment.ContentRootPath, configured)));

    // La raíz no puede caer dentro de `wwwroot`: ahí `UseStaticFiles` serviría cada
    // comprobante a quien adivinara la ruta. Nada más lo impide, así que se comprueba.
    var webRoot = string.IsNullOrEmpty(environment.WebRootPath)
        ? null
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath));

    if (webRoot is not null && IsInside(webRoot, _root))
      throw new InvalidOperationException(
          $"Documents:RootPath ('{_root}') is inside the web root ('{webRoot}'). " +
          "Private documents must NOT be served as static files.");

    // La raíz se resuelve ya por si ella misma es un enlace: si no, la comparación de
    // TryResolve mezclaría una ruta real con otra que aún pasa por el enlace.
    _root = FinalTargetOf(_root);
  }

  /// <inheritdoc />
  public async Task<DocumentReference> SaveAsync(
      DocumentContent content, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(content);

    var now = DateTime.Now;

    // La clave no se deriva de la orden ni del usuario: si fuera predecible, un despiste
    // en el control de acceso sería una fuga masiva y no la de un documento.
    var key = $"{now:yyyy}/{now:MM}/{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{ExtensionFor(content.ContentType)}";

    var path = ResolvePath(key);

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    try
    {
      // CreateNew y no Create: si la clave ya existiera es mejor reventar que pisar el
      // comprobante de otro en silencio.
      await using var file = new FileStream(
          path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

      await content.Stream.CopyToAsync(file, ct);
    }
    catch
    {
      // Un fallo a mitad de la copia deja un PDF truncado que nadie referencia: se borra
      // aquí, que es donde se sabe que está corrupto.
      TryDelete(path);
      throw;
    }

    var size = new FileInfo(path).Length;

    _logger.LogInformation("Stored document {Key} ({Size} bytes)", key, size);

    return new DocumentReference(key, size);
  }

  /// <inheritdoc />
  public Task<DocumentContent?> OpenAsync(string key, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    if (!TryResolve(key, out var path)) return Task.FromResult<DocumentContent?>(null);

    try
    {
      // Se abre directamente en vez de comprobar `File.Exists` antes: entre las dos cosas
      // cabe un borrado, y la excepción resultante sería un 500 en vez del null prometido.
      var stream = new FileStream(
          path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920, useAsync: true);

      return Task.FromResult<DocumentContent?>(
          new DocumentContent(stream, ContentTypeFor(path), Path.GetFileName(path)));
    }
    catch (IOException)
    {
      // FileNotFoundException y DirectoryNotFoundException heredan de esta.
      return Task.FromResult<DocumentContent?>(null);
    }
  }

  /// <inheritdoc />
  public Task DeleteAsync(string key, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    if (TryResolve(key, out var path)) TryDelete(path);

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public async IAsyncEnumerable<DocumentEntry> ListAsync(
      DateTime writtenBefore,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
  {
    if (!Directory.Exists(_root)) yield break;

    // EnumerateFiles y no GetFiles: devuelve perezosamente, sin materializar el almacén.
    foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
    {
      ct.ThrowIfCancellationRequested();

      FileInfo info;

      try
      {
        info = new FileInfo(path);

        // `LastWriteTime` y no `CreationTime`: la fecha de creación no se mantiene al
        // copiar o restaurar un volumen, y aquí «antigua» significa «bórralo».
        if (info.LastWriteTime >= writtenBefore) continue;
      }
      catch (IOException)
      {
        continue;   // desapareció mientras enumerábamos: no es nuestro problema
      }

      // La clave es la ruta relativa con '/', la misma forma que devolvió SaveAsync: dar
      // la absoluta convertiría al recolector en alguien que conoce el disco.
      yield return new DocumentEntry(
          Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/'),
          info.LastWriteTime,
          info.Length);
    }
  }

  /// <summary>Borra si está. Idempotente y sin carrera entre el <c>Exists</c> y el borrado.</summary>
  private static void TryDelete(string path)
  {
    try { File.Delete(path); }          // no lanza si no existe
    catch (IOException) { /* ya no está, o lo tiene otro abierto */ }
    catch (UnauthorizedAccessException) { /* sin permiso: no es cosa de quien llama */ }
  }

  // ---- resolución de rutas -------------------------------------------------

  /// <summary>Ruta absoluta de una clave, comprobando que no se sale del almacén.</summary>
  /// <remarks>
  /// Es el control de seguridad de la clase: se compara la ruta ya canonicalizada contra
  /// la raíz, porque filtrar por la cadena <c>".."</c> no cubre las rutas absolutas, y se
  /// siguen los enlaces simbólicos, que <c>Path.GetFullPath</c> no resuelve.
  /// </remarks>
  private bool TryResolve(string key, out string path)
  {
    path = string.Empty;

    if (string.IsNullOrWhiteSpace(key)) return false;

    var candidate = Path.GetFullPath(Path.Combine(_root, key));

    // El separador final impide que "/data/docs-otro" pase por estar dentro de "/data/docs".
    if (!IsInside(_root, candidate) || !IsInside(_root, ResolveSymlinks(candidate)))
    {
      _logger.LogWarning("Rejected document key that escapes the store root: {Key}", key);
      return false;
    }

    path = candidate;

    return true;
  }

  /// <summary>¿Está <paramref name="candidate"/> dentro de <paramref name="root"/>?</summary>
  /// <remarks>
  /// El separador es lo que impide que <c>/data/docs-otro</c> pase por estar «dentro» de
  /// <c>/data/docs</c>: sin él basta un directorio hermano con el mismo prefijo.
  /// </remarks>
  private static bool IsInside(string root, string candidate)
      => candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);

  /// <summary>
  /// Ruta real de <paramref name="path"/>, siguiendo los enlaces de cada segmento bajo la raíz.
  /// </summary>
  /// <remarks>
  /// Segmento a segmento y no solo el final: un enlace intermedio saca la ruta del almacén
  /// igual, y <c>ResolveLinkTarget</c> solo mira el último componente. Los segmentos que
  /// aún no existen se concatenan tal cual, porque no pueden ser enlaces todavía.
  /// </remarks>
  private string ResolveSymlinks(string path)
  {
    var current = _root;

    foreach (var segment in Path.GetRelativePath(_root, path)
                                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
    {
      if (segment.Length == 0) continue;

      current = FinalTargetOf(Path.Combine(current, segment));
    }

    return current;
  }

  /// <summary>Destino final de un enlace, o la propia ruta si no lo es (o no existe).</summary>
  private static string FinalTargetOf(string path)
  {
    try
    {
      var target = Directory.Exists(path)
          ? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
          : File.Exists(path) ? File.ResolveLinkTarget(path, returnFinalTarget: true) : null;

      return target is null ? path : Path.TrimEndingDirectorySeparator(target.FullName);
    }
    catch (IOException)
    {
      // Enlace circular o roto: se devuelve algo que no está dentro y decide quien compara.
      return Path.GetTempPath();
    }
  }

  /// <summary>Como <see cref="TryResolve"/>, pero para escribir: una clave mala es un bug.</summary>
  private string ResolvePath(string key)
      => TryResolve(key, out var path)
          ? path
          : throw new InvalidOperationException($"The generated document key is not valid: '{key}'.");

  private static string ExtensionFor(string contentType) => contentType switch
  {
    "application/pdf" => ".pdf",
    _ => ".bin"
  };

  private static string ContentTypeFor(string path) => Path.GetExtension(path) switch
  {
    ".pdf" => "application/pdf",
    _ => "application/octet-stream"
  };
}
