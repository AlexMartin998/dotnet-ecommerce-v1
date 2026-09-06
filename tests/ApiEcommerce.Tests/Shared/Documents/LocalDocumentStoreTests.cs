using System.Text;
using ApiEcommerce.Shared.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Tests.Shared.Documents;


/// <summary>
/// El almacén de documentos <b>privados</b>: comprobantes que llevan el nombre del
/// cliente, su dirección y lo que pagó.
/// </summary>
/// <remarks>
/// Cada test de aquí cubre una decisión de diseño con consecuencias, no una comprobación
/// de formulario: que la clave no se pueda adivinar, que no se pueda salir de la raíz, y
/// que «no está» sea una condición tratable y no una excepción.
/// </remarks>
public sealed class LocalDocumentStoreTests : IDisposable
{
  private readonly string _root =
      Path.Combine(Path.GetTempPath(), $"apiecommerce-docs-{Guid.NewGuid():N}");

  private LocalDocumentStore Sut(string? rootPath = null, string? webRoot = null) => new(
      Options.Create(new DocumentStorageOptions { RootPath = rootPath ?? _root }),
      Environment(webRoot),
      NullLogger<LocalDocumentStore>.Instance);

  /// <summary>Content root (para rutas relativas) y web root (para la comprobación de wwwroot).</summary>
  private IWebHostEnvironment Environment(string? webRoot = null)
  {
    var environment = new Mock<IWebHostEnvironment>();

    environment.SetupGet(e => e.ContentRootPath).Returns(Path.GetDirectoryName(_root)!);
    environment.SetupGet(e => e.WebRootPath).Returns(webRoot ?? Path.Combine(_root, "..", "wwwroot-inexistente"));

    return environment.Object;
  }

  private static DocumentContent Pdf(string body = "%PDF-1.4 fake")
      => new(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/pdf", "ORD-2026-000001.pdf");

  public void Dispose()
  {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  // ---- guardar y recuperar -------------------------------------------------

  [Fact]
  public async Task SaveAsync_ReturnsAKeyThatOpensTheSameContent()
  {
    var store = Sut();

    var saved = await store.SaveAsync(Pdf("contenido del comprobante"));

    await using var opened = await store.OpenAsync(saved.Key);

    Assert.NotNull(opened);
    Assert.Equal("application/pdf", opened.ContentType);
    Assert.Equal("contenido del comprobante", await new StreamReader(opened.Stream).ReadToEndAsync());
  }

  [Fact]
  public async Task SaveAsync_WritesOutsideAnyPubliclyServedFolder()
  {
    // ⚠️ La razón de que este almacén exista aparte de IFileStorage. Si el fichero
    // acabara bajo wwwroot/, UseStaticFiles lo serviría a quien adivinara la ruta y las
    // tres barreras de acceso se quedarían en dos.
    var saved = await Sut().SaveAsync(Pdf());

    var written = Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories);

    Assert.Single(written);
    Assert.DoesNotContain("wwwroot", written[0]);
    Assert.EndsWith(saved.Key.Replace('/', Path.DirectorySeparatorChar), written[0]);
  }

  [Fact]
  public async Task SaveAsync_ReportsTheSizeSoItCanBeServedWithoutOpeningIt()
  {
    var saved = await Sut().SaveAsync(Pdf("1234567890"));

    Assert.Equal(10, saved.SizeBytes);
  }

  [Fact]
  public async Task SaveAsync_GivesEveryDocumentAnUnpredictableKey()
  {
    // La clave NO se deriva de la orden ni del usuario: si fuera deducible, cualquier
    // despiste futuro en el control de acceso pasaría de filtrar un documento a filtrarlos
    // todos.
    var store = Sut();

    var keys = new HashSet<string>();

    for (var i = 0; i < 20; i++) keys.Add((await store.SaveAsync(Pdf())).Key);

    Assert.Equal(20, keys.Count);
  }

  // ---- traversal -----------------------------------------------------------

  [Theory]
  [InlineData("../../appsettings.json")]
  [InlineData("../secreto.pdf")]
  [InlineData("/etc/passwd")]
  [InlineData("2026/09/../../../fuera.pdf")]
  [InlineData("")]
  public async Task OpenAsync_WithAKeyThatEscapesTheRoot_ReturnsNull(string key)
  {
    // La clave viene de la base, pero basta una fila manipulada, una migración descuidada
    // o un endpoint futuro que la acepte del cliente. Se compara la ruta ya
    // canonicalizada: filtrar por la cadena ".." no cubre rutas absolutas.
    Assert.Null(await Sut().OpenAsync(key));
  }

  [Fact]
  public async Task OpenAsync_WithASiblingFolderThatSharesThePrefix_ReturnsNull()
  {
    // El separador final del chequeo es lo que impide que "/tmp/docs-otro" pase por estar
    // "dentro" de "/tmp/docs". Sin él, la comprobación se salta con un nombre parecido.
    Assert.Null(await Sut().OpenAsync("../" + Path.GetFileName(_root) + "-otro/x.pdf"));
  }

  // ---- ausencias -----------------------------------------------------------

  [Fact]
  public async Task OpenAsync_WhenTheDocumentIsNotThere_ReturnsNullInsteadOfThrowing()
  {
    // Que un documento no esté es una condición que quien llama tiene que poder tratar
    // —acaba en un 404, no en un 500—, no un fallo del sistema.
    Assert.Null(await Sut().OpenAsync("2026/09/noexiste.pdf"));
  }

  [Fact]
  public async Task DeleteAsync_IsIdempotent()
  {
    var store = Sut();
    var saved = await store.SaveAsync(Pdf());

    await store.DeleteAsync(saved.Key);
    await store.DeleteAsync(saved.Key);   // segunda vez: no lanza

    Assert.Null(await store.OpenAsync(saved.Key));
  }

  [Fact]
  public async Task DeleteAsync_WithAKeyThatEscapesTheRoot_DoesNothing()
  {
    var outside = Path.Combine(Path.GetTempPath(), $"no-tocar-{Guid.NewGuid():N}.txt");
    await File.WriteAllTextAsync(outside, "intacto");

    try
    {
      await Sut().DeleteAsync(Path.GetRelativePath(_root, outside));

      Assert.True(File.Exists(outside));
    }
    finally
    {
      File.Delete(outside);
    }
  }

  [Fact]
  public async Task ARelativeRootIsResolvedAgainstTheContentRootAndNotTheWorkingDirectory()
  {
    // ⚠️ `Path.GetFullPath` usa el DIRECTORIO DE TRABAJO. Con eso, dónde acaban los
    // comprobantes dependería de desde dónde se lanzó el proceso —un
    // `dotnet /app/ApiEcommerce.dll` desde otra carpeta escribiría en otro sitio— y todas
    // las claves ya guardadas darían 404.
    var relative = Path.GetFileName(_root);

    var saved = await Sut(relative).SaveAsync(Pdf("resuelto contra el content root"));

    Assert.True(File.Exists(Path.Combine(_root, saved.Key.Replace('/', Path.DirectorySeparatorChar))));
  }

  // ---- enlaces simbólicos --------------------------------------------------

  [Fact]
  public async Task OpenAsync_ThroughASymlinkThatLeavesTheRoot_ReturnsNull()
  {
    // ⚠️ `Path.GetFullPath` normaliza `.` y `..` pero **no sigue los enlaces**. Sin
    // resolverlos, un enlace dentro del almacén (`2026 -> ../secretos`) produce una ruta
    // que EMPIEZA por la raíz, pasa el filtro, y lee un fichero de fuera. Lo destapó la
    // revisión de seguridad reproduciéndolo, no leyéndolo.
    var secrets = Path.Combine(Path.GetTempPath(), $"apiecommerce-secretos-{Guid.NewGuid():N}");
    Directory.CreateDirectory(secrets);
    await File.WriteAllTextAsync(Path.Combine(secrets, "robado.pdf"), "no deberia leerse");

    Directory.CreateDirectory(_root);
    Directory.CreateSymbolicLink(Path.Combine(_root, "2026"), secrets);

    try
    {
      Assert.Null(await Sut().OpenAsync("2026/robado.pdf"));
    }
    finally
    {
      Directory.Delete(secrets, recursive: true);
    }
  }

  [Fact]
  public async Task OpenAsync_ThroughASymlinkThatStaysInsideTheRoot_StillWorks()
  {
    // La otra mitad: resolver enlaces no puede romper un almacén que use uno por dentro
    // (un montaje, una carpeta movida). Solo se rechaza lo que SALE de la raíz.
    var store = Sut();
    var saved = await store.SaveAsync(Pdf("dentro del almacén"));

    var real = Path.Combine(_root, "real");
    Directory.CreateDirectory(real);
    File.Move(Path.Combine(_root, saved.Key.Replace('/', Path.DirectorySeparatorChar)),
              Path.Combine(real, "doc.pdf"));

    Directory.Delete(Path.GetDirectoryName(Path.Combine(_root, saved.Key.Replace('/', Path.DirectorySeparatorChar)))!);
    Directory.CreateSymbolicLink(
        Path.GetDirectoryName(Path.Combine(_root, saved.Key.Replace('/', Path.DirectorySeparatorChar)))!, real);

    await using var opened = await store.OpenAsync(
        Path.GetDirectoryName(saved.Key)!.Replace(Path.DirectorySeparatorChar, '/') + "/doc.pdf");

    Assert.NotNull(opened);
  }

  // ---- configuración que rompe el almacén entero ---------------------------

  [Fact]
  public async Task ARootWithATrailingSeparator_StillWorks()
  {
    // ⚠️ `GetFullPath` CONSERVA la barra final y la comprobación concatena una: sin
    // recortarla, la raíz quedaba como ".../docs//" y NINGUNA clave pasaba el filtro.
    // Guardar lanzaría y abrir devolvería null para todo — todos los comprobantes en 404
    // permanente mientras la orden dice "available". Un carácter, y en silencio.
    var store = Sut(_root + Path.DirectorySeparatorChar);

    var saved = await store.SaveAsync(Pdf("con barra final"));

    Assert.NotNull(await store.OpenAsync(saved.Key));
  }

  [Fact]
  public void ARootInsideTheWebRoot_RefusesToStart()
  {
    // Es toda la premisa de esta clase: dentro de wwwroot, UseStaticFiles serviría cada
    // comprobante a quien adivinara la ruta. Nada más lo impide —es una cadena en un
    // fichero de configuración— así que se comprueba y se revienta.
    var webRoot = Path.GetDirectoryName(_root)!;

    var boom = Assert.Throws<InvalidOperationException>(() => Sut(webRoot: webRoot));

    Assert.Contains("web root", boom.Message);
  }
}
