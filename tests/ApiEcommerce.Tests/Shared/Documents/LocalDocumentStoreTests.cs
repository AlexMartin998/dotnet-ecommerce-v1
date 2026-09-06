using System.Text;
using ApiEcommerce.Shared.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Tests.Shared.Documents;


/// <summary>
/// El almacén de documentos privados: comprobantes con nombre, dirección e importe.
/// </summary>
/// <remarks>
/// Cubre las tres decisiones que lo sostienen: clave no adivinable, imposible salir de la
/// raíz, y «no está» como condición tratable en vez de excepción.
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
    // Es la razón de que este almacén exista aparte de IFileStorage: bajo wwwroot/,
    // UseStaticFiles serviría el comprobante a quien adivinara la ruta.
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
    // Si la clave fuera deducible, un despiste en el control de acceso pasaría de filtrar
    // un documento a filtrarlos todos.
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
    // Se compara la ruta ya canonicalizada: filtrar por la cadena ".." no cubre rutas
    // absolutas, y basta una fila manipulada para que la clave no sea de fiar.
    Assert.Null(await Sut().OpenAsync(key));
  }

  [Fact]
  public async Task OpenAsync_WithASiblingFolderThatSharesThePrefix_ReturnsNull()
  {
    // El separador final impide que "/tmp/docs-otro" pase por estar dentro de "/tmp/docs".
    Assert.Null(await Sut().OpenAsync("../" + Path.GetFileName(_root) + "-otro/x.pdf"));
  }

  // ---- ausencias -----------------------------------------------------------

  [Fact]
  public async Task OpenAsync_WhenTheDocumentIsNotThere_ReturnsNullInsteadOfThrowing()
  {
    // Que no esté es una condición tratable —acaba en 404—, no un fallo del sistema.
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
    // `Path.GetFullPath` resuelve contra el directorio de trabajo: sin fijar la base, dónde
    // acaban los comprobantes dependería de desde dónde se lanzó el proceso.
    var relative = Path.GetFileName(_root);

    var saved = await Sut(relative).SaveAsync(Pdf("resuelto contra el content root"));

    Assert.True(File.Exists(Path.Combine(_root, saved.Key.Replace('/', Path.DirectorySeparatorChar))));
  }

  // ---- enlaces simbólicos --------------------------------------------------

  [Fact]
  public async Task OpenAsync_ThroughASymlinkThatLeavesTheRoot_ReturnsNull()
  {
    // `Path.GetFullPath` normaliza `.` y `..` pero no sigue los enlaces: sin resolverlos,
    // un `2026 -> ../secretos` da una ruta que empieza por la raíz y lee de fuera.
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
    // Resolver enlaces no puede romper un almacén que use uno por dentro (un montaje):
    // solo se rechaza lo que sale de la raíz.
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
    // `GetFullPath` conserva la barra final y la comprobación concatena otra: sin
    // recortarla ninguna clave pasa el filtro y todo comprobante da 404 en silencio.
    var store = Sut(_root + Path.DirectorySeparatorChar);

    var saved = await store.SaveAsync(Pdf("con barra final"));

    Assert.NotNull(await store.OpenAsync(saved.Key));
  }

  [Fact]
  public void ARootInsideTheWebRoot_RefusesToStart()
  {
    // Dentro de wwwroot, UseStaticFiles serviría cada comprobante a quien adivinase la
    // ruta, y nada más lo impide: es una cadena en un fichero de configuración.
    var webRoot = Path.GetDirectoryName(_root)!;

    var boom = Assert.Throws<InvalidOperationException>(() => Sut(webRoot: webRoot));

    Assert.Contains("web root", boom.Message);
  }
}
