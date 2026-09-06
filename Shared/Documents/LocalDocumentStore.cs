using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Documents;


/// <summary>
/// Almacén de documentos sobre el sistema de ficheros. La implementación de hoy.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ Escribe <b>fuera de <c>wwwroot/</c></b>, y eso no es un detalle: dentro,
/// <c>UseStaticFiles</c> serviría cada comprobante a cualquiera que adivinara la ruta, sin
/// pasar por autenticación. Se sirven por un endpoint que comprueba de quién es la orden.
/// </para>
/// <para>
/// La clave tiene forma <c>aaaa/mm/&lt;32 hex&gt;.pdf</c>. Las carpetas por año y mes no son
/// estética: un único directorio con cientos de miles de ficheros hace lento hasta un
/// <c>ls</c>, y en algunos sistemas de ficheros degrada la apertura. La parte aleatoria son
/// 16 bytes de un CSPRNG.
/// </para>
/// </remarks>
public sealed class LocalDocumentStore : IDocumentStore
{
  private readonly ILogger<LocalDocumentStore> _logger;

  /// <summary>Raíz del almacén, ya absoluta.</summary>
  /// <remarks>
  /// ⚠️ Una ruta relativa se resuelve contra el <b>content root</b>, no contra el
  /// directorio de trabajo. `Path.GetFullPath` usa el <c>cwd</c>, y eso hace que dónde
  /// acaban los comprobantes dependa de <i>desde dónde</i> se arrancó el proceso: un
  /// `dotnet /app/ApiEcommerce.dll` lanzado desde otra carpeta escribiría en otro sitio y
  /// **todas las claves ya guardadas darían 404**. Se resuelve una sola vez, aquí.
  /// </remarks>
  private readonly string _root;

  public LocalDocumentStore(
      IOptions<DocumentStorageOptions> options,
      IWebHostEnvironment environment,
      ILogger<LocalDocumentStore> logger)
  {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(environment);

    _logger = logger;

    var configured = options.Value.RootPath;

    // ⚠️ `TrimEndingDirectorySeparator`: `GetFullPath` CONSERVA la barra final, y la
    // comprobación de abajo concatena una. Con `RootPath` acabado en "/" la raíz quedaba
    // como ".../docs//" y **ninguna clave** pasaba el filtro: guardar lanzaría y abrir
    // devolvería null para todo, o sea todos los comprobantes en 404 permanente mientras
    // la orden dice "available". Un carácter de más en la configuración, y en silencio.
    _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(configured)
        ? configured
        : Path.Combine(environment.ContentRootPath, configured)));

    // ⚠️ Y la raíz NO puede caer dentro de `wwwroot`. Es toda la premisa de esta clase:
    // ahí `UseStaticFiles` serviría cada comprobante a quien adivinara la ruta, saltándose
    // la autenticación. Nada más lo impide —es una cadena en un fichero de configuración—
    // así que se comprueba, y se revienta en vez de servir documentos privados en abierto.
    var webRoot = string.IsNullOrEmpty(environment.WebRootPath)
        ? null
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(environment.WebRootPath));

    if (webRoot is not null && IsInside(webRoot, _root))
      throw new InvalidOperationException(
          $"Documents:RootPath ('{_root}') is inside the web root ('{webRoot}'). " +
          "Private documents must NOT be served as static files.");

    // La raíz se resuelve YA por si ella misma es un enlace: si no, la comparación de
    // TryResolve mezclaría una ruta real con una que aún pasa por el enlace.
    _root = FinalTargetOf(_root);
  }

  public async Task<DocumentReference> SaveAsync(
      DocumentContent content, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(content);

    var now = DateTime.Now;

    // La clave NO se deriva de la orden ni del usuario: si fuera predecible, cualquier
    // despiste futuro en el control de acceso se convertiría en una fuga masiva en vez de
    // en una filtración de un documento.
    var key = $"{now:yyyy}/{now:MM}/{Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant()}{ExtensionFor(content.ContentType)}";

    var path = ResolvePath(key);

    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    try
    {
      // FileMode.CreateNew y no Create: si la clave ya existiera —lo que solo puede pasar
      // por un fallo del generador aleatorio— es mejor reventar que pisar el comprobante de
      // otro en silencio.
      await using var file = new FileStream(
          path, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

      await content.Stream.CopyToAsync(file, ct);
    }
    catch
    {
      // ⚠️ Un fallo a mitad de la copia deja un PDF **truncado** en disco. Nadie lo
      // referencia —`SaveAsync` no llegó a devolver clave—, así que sería un huérfano más;
      // la diferencia es que este está CORRUPTO, y el día que exista el recolector tendría
      // que distinguirlo. Se borra aquí, que es donde se sabe.
      TryDelete(path);
      throw;
    }

    var size = new FileInfo(path).Length;

    _logger.LogInformation("Stored document {Key} ({Size} bytes)", key, size);

    return new DocumentReference(key, size);
  }

  public Task<DocumentContent?> OpenAsync(string key, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    if (!TryResolve(key, out var path)) return Task.FromResult<DocumentContent?>(null);

    try
    {
      // ⚠️ Se ABRE directamente en vez de comprobar `File.Exists` y abrir después: entre
      // las dos cosas cabe un borrado, y entonces salía una `FileNotFoundException`
      // desnuda → **500**. Esta interfaz promete devolver `null` cuando no está, y quien
      // llama ya sabe traducirlo a un 404 honesto. La comprobación previa no evita la
      // carrera, solo la hace menos frecuente y por tanto más difícil de diagnosticar.
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

  public Task DeleteAsync(string key, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    if (TryResolve(key, out var path)) TryDelete(path);

    return Task.CompletedTask;
  }

  public async IAsyncEnumerable<DocumentEntry> ListAsync(
      DateTime writtenBefore,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
  {
    if (!Directory.Exists(_root)) yield break;

    // EnumerateFiles y no GetFiles: devuelve perezosamente, así que un almacén con muchos
    // ficheros no se materializa entero en memoria antes de mirar el primero.
    foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
    {
      ct.ThrowIfCancellationRequested();

      FileInfo info;

      try
      {
        info = new FileInfo(path);

        // Se lee `LastWriteTime` y no `CreationTime`: en varios sistemas de ficheros la
        // fecha de creación no se mantiene al copiar o restaurar un volumen, y aquí una
        // fecha demasiado antigua significa "bórralo".
        if (info.LastWriteTime >= writtenBefore) continue;
      }
      catch (IOException)
      {
        continue;   // desapareció mientras enumerábamos: no es nuestro problema
      }

      // La clave es la ruta relativa a la raíz, con '/' — la misma forma que devolvió
      // SaveAsync. Devolver la ruta absoluta convertiría al recolector en alguien que
      // conoce el disco, que es justo lo que la clave opaca evita.
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
  /// <para>
  /// ⚠️ <b>Esta comprobación es el control de seguridad de la clase.</b> La clave viaja
  /// desde la base de datos, pero basta una fila manipulada, un endpoint futuro que la
  /// acepte del cliente o una migración descuidada para que llegue algo como
  /// <c>../../appsettings.json</c>. Se compara la ruta ya <b>canonicalizada</b> contra la
  /// raíz: filtrar por la cadena <c>".."</c> no vale, porque no cubre las rutas absolutas.
  /// </para>
  /// <para>
  /// ⚠️ Y <b>canonicalizar no es solo normalizar</b>: <c>Path.GetFullPath</c> resuelve
  /// <c>.</c> y <c>..</c> pero <b>no sigue los enlaces simbólicos</b>. Con un enlace dentro
  /// del almacén (<c>2026 → ../secretos</c>), la ruta normalizada empieza por la raíz, pasa
  /// el filtro, y se lee un fichero de fuera. Por eso se resuelve el destino real
  /// <b>segmento a segmento</b> antes de comparar. El ataque exige poder escribir en el
  /// directorio del almacén, así que hoy es remoto — pero esta comprobación es lo único que
  /// hay, y una que dice cubrir algo que no cubre es peor que no tenerla.
  /// </para>
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
  /// Ruta real de <paramref name="path"/>, siguiendo los enlaces de <b>cada</b> segmento
  /// bajo la raíz.
  /// </summary>
  /// <remarks>
  /// Se recorre segmento a segmento y no solo el final: un enlace en un directorio
  /// intermedio saca la ruta del almacén igual, y <c>ResolveLinkTarget</c> solo mira el
  /// último componente. Los segmentos que aún no existen —lo normal al guardar— se
  /// concatenan tal cual: no pueden ser enlaces todavía.
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
      // Un enlace circular o roto. No se puede decir que esté dentro, así que se devuelve
      // algo que NO lo está: la decisión la toma quien compara, no este helper.
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
