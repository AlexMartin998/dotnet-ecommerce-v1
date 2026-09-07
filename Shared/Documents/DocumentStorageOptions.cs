using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Documents;


/// <summary>Almacén de documentos privados, sección <c>Documents</c>.</summary>
public sealed class DocumentStorageOptions
{
  public const string SectionName = "Documents";

  /// <summary>
  /// Qué implementación se usa. Hoy solo <c>filesystem</c>.
  /// </summary>
  /// <remarks>
  /// Es el interruptor para cambiar de infraestructura sin tocar código. Un valor
  /// desconocido tumba el arranque en vez de caer a disco, para no creer que se escribe en
  /// un bucket cuando se escribe en un contenedor efímero.
  /// </remarks>
  [Required]
  public string Provider { get; init; } = FileSystemProvider;

  /// <summary>Valor de <see cref="Provider"/> para el sistema de ficheros local.</summary>
  public const string FileSystemProvider = "filesystem";

  /// <summary>
  /// Carpeta raíz cuando el proveedor es el sistema de ficheros.
  /// </summary>
  /// <remarks>
  /// Fuera de <c>wwwroot/</c>, o <c>UseStaticFiles</c> serviría cada comprobante a quien
  /// adivinara la ruta; y en un despliegue real, sobre un volumen.
  /// </remarks>
  [Required]
  public string RootPath { get; init; } = "App_Data/documents";

  /// <summary>
  /// Cada cuánto pasa el recolector de huérfanos. 0 lo apaga.
  /// </summary>
  /// <remarks>
  /// Poder apagarlo sin desplegar importa porque es un job que borra ficheros.
  /// </remarks>
  [Range(0, 168)]
  public int CleanupIntervalHours { get; init; } = 12;

  /// <summary>
  /// Cuánto tiene que llevar escrito un documento antes de que se pueda considerar huérfano.
  /// </summary>
  /// <remarks>
  /// El documento se escribe dentro de la transacción que lo referencia, así que existe un
  /// rato antes que su fila: sin gracia, el recolector borraría comprobantes buenos a
  /// mitad de vuelo. El valor por defecto sobra por varios órdenes de magnitud a propósito.
  /// </remarks>
  [Range(1, 720)]
  public int OrphanGraceHours { get; init; } = 24;
}
