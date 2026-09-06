using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace ApiEcommerce.Tests.Shared.Storage;


/// <summary>
/// Subida de imágenes. Cada test corresponde a una vía de ataque, no a una comprobación
/// de formulario.
/// </summary>
/// <remarks>
/// Escribe en una carpeta temporal propia y la borra al terminar: el disco es la única
/// dependencia y no merece un contenedor.
/// </remarks>
public sealed class LocalFileStorageTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), $"apiecommerce-tests-{Guid.NewGuid():N}");
  private readonly FileStorageOptions _options = new() { ProductImagesFolder = "ProductsImages", MaxBytes = 1024 };

  private LocalFileStorage Sut()
  {
    var environment = new Mock<IWebHostEnvironment>();
    environment.SetupGet(e => e.WebRootPath).Returns(_root);

    return new LocalFileStorage(
        environment.Object, Options.Create(_options), NullLogger<LocalFileStorage>.Instance);
  }

  public void Dispose()
  {
    if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
  }

  // ---- validación ---------------------------------------------------------

  [Fact]
  public async Task SaveProductImageAsync_WithAnEmptyFile_ThrowsBadRequest()
      => await Assert.ThrowsAsync<BadOperationAppException>(
          () => Sut().SaveProductImageAsync(Upload([], "foto.png")));

  [Fact]
  public async Task SaveProductImageAsync_OverTheSizeLimit_ThrowsBadRequest()
  {
    var ex = await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().SaveProductImageAsync(Upload(Png(2048), "foto.png")));

    Assert.Contains("maximum size", ex.Message);
  }

  [Theory]
  [InlineData("script.svg")]     // SVG lleva JavaScript y se sirve desde nuestro origen: XSS almacenado
  [InlineData("pagina.html")]
  [InlineData("shell.php")]
  [InlineData("sin-extension")]
  public async Task SaveProductImageAsync_WithADisallowedExtension_ThrowsBadRequest(string fileName)
  {
    // Allowlist, no denylist: una denylist siempre se olvida de algo.
    var ex = await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().SaveProductImageAsync(Upload(Png(64), fileName)));

    Assert.Contains("Unsupported file type", ex.Message);
  }

  [Fact]
  public async Task SaveProductImageAsync_WhenTheContentIsNotTheDeclaredImage_ThrowsBadRequest()
  {
    // Extensión y Content-Type los pone el cliente y puede mentir en los dos; la firma
    // del archivo, no.
    var text = System.Text.Encoding.UTF8.GetBytes("esto no es una imagen, es texto plano");

    var ex = await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().SaveProductImageAsync(Upload(text, "foto.png", "image/png")));

    Assert.Contains("does not match", ex.Message);
  }

  [Fact]
  public async Task SaveProductImageAsync_WithAFileTooShortToHaveASignature_ThrowsBadRequest()
  {
    // La firma se lee de 12 bytes: lo más corto no se puede verificar, así que se rechaza.
    await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().SaveProductImageAsync(Upload([0x89, 0x50, 0x4E, 0x47], "foto.png")));
  }

  // ---- camino feliz -------------------------------------------------------

  [Fact]
  public async Task SaveProductImageAsync_GeneratesTheNameOnTheServer()
  {
    // Del nombre que manda el cliente solo se lee la extensión: lo demás es la puerta de
    // entrada al path traversal.
    var path = await Sut().SaveProductImageAsync(Upload(Png(64), "../../../etc/passwd.png"));

    Assert.StartsWith("/ProductsImages/", path);
    Assert.DoesNotContain("passwd", path);
    Assert.DoesNotContain("..", path);
    Assert.EndsWith(".png", path);

    // Y el archivo está DENTRO de la carpeta gestionada, no en cualquier sitio.
    var written = Directory.GetFiles(Path.Combine(_root, "ProductsImages"));
    Assert.Single(written);
  }

  [Fact]
  public async Task SaveProductImageAsync_ReturnsARelativePathAndNeverAnAbsoluteUrl()
  {
    // Persistir una URL absoluta guardaría una cabecera que controla el cliente, y se
    // rompería al cambiar de dominio o al poner un proxy delante.
    var path = await Sut().SaveProductImageAsync(Upload(Png(64), "foto.PNG"));

    Assert.StartsWith("/", path);
    Assert.DoesNotContain("http", path);
    Assert.EndsWith(".png", path);   // la extensión se normaliza a minúsculas
  }

  [Theory]
  [InlineData("foto.jpg", new byte[] { 0xFF, 0xD8, 0xFF })]
  [InlineData("foto.gif", new byte[] { 0x47, 0x49, 0x46, 0x38 })]
  public async Task SaveProductImageAsync_AcceptsEveryAllowedFormat(string fileName, byte[] signature)
  {
    var content = new byte[64];
    signature.CopyTo(content, 0);

    Assert.StartsWith("/ProductsImages/", await Sut().SaveProductImageAsync(Upload(content, fileName)));
  }

  // ---- borrado ------------------------------------------------------------

  [Fact]
  public async Task DeleteAsync_RemovesAFileItManages()
  {
    var sut = Sut();
    var path = await sut.SaveProductImageAsync(Upload(Png(64), "foto.png"));

    await sut.DeleteAsync(path);

    Assert.Empty(Directory.GetFiles(Path.Combine(_root, "ProductsImages")));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("https://cdn.ajeno.com/foto.png")]     // URL externa: no es nuestra
  [InlineData("/OtraCarpeta/foto.png")]              // fuera de la carpeta gestionada
  public async Task DeleteAsync_IgnoresWhatIsNotItsOwn(string? path)
  {
    // No lanza: un DELETE de producto no puede fallar porque su ImageUrl fuera externa.
    await Sut().DeleteAsync(path);
  }

  [Fact]
  public async Task DeleteAsync_OnAMissingFile_IsIdempotent()
      => await Sut().DeleteAsync("/ProductsImages/no-existe.png");

  // ---- helpers ------------------------------------------------------------

  private static FileUpload Upload(byte[] content, string fileName, string contentType = "image/png")
      => new(new MemoryStream(content), fileName, contentType, content.Length);

  /// <summary>PNG mínimo válido: firma correcta y relleno hasta el tamaño pedido.</summary>
  private static byte[] Png(int size)
  {
    var bytes = new byte[size];
    new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
    return bytes;
  }
}
