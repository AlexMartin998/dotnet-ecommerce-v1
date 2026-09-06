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
  /// <para>
  /// Es el interruptor por el que se cambia de infraestructura sin tocar código: el día
  /// que exista <c>S3DocumentStore</c>, poner <c>"s3"</c> aquí es todo lo que hay que
  /// hacer. Ni el dominio, ni el consumidor que genera el comprobante, ni el controller
  /// que lo sirve se enteran.
  /// </para>
  /// <para>
  /// ⚠️ Un valor desconocido <b>tumba el arranque</b>, no cae a un valor por defecto.
  /// Caer al sistema de ficheros ante un <c>"s3"</c> mal escrito significaría escribir
  /// comprobantes en el disco de un contenedor efímero creyendo que están en el bucket, y
  /// enterarse el día que se reinicie.
  /// </para>
  /// </remarks>
  [Required]
  public string Provider { get; init; } = FileSystemProvider;

  public const string FileSystemProvider = "filesystem";

  /// <summary>
  /// Carpeta raíz cuando el proveedor es el sistema de ficheros.
  /// </summary>
  /// <remarks>
  /// ⚠️ <b>Fuera de <c>wwwroot/</c></b>. Dentro, <c>UseStaticFiles</c> serviría cada
  /// comprobante a quien adivinara la ruta, saltándose la autenticación entera.
  /// ⚠️ Y fuera del árbol de la aplicación en un despliegue real: en un contenedor tiene
  /// que ser un volumen, o los comprobantes se van con el contenedor.
  /// </remarks>
  [Required]
  public string RootPath { get; init; } = "App_Data/documents";

  /// <summary>
  /// Cada cuánto pasa el recolector de huérfanos. <b>0 lo apaga.</b>
  /// </summary>
  /// <remarks>
  /// Poder apagarlo sin desplegar no es un lujo: es un job que <b>borra ficheros</b>, y ante
  /// cualquier sospecha lo primero que uno quiere es pararlo.
  /// </remarks>
  [Range(0, 168)]
  public int CleanupIntervalHours { get; init; } = 12;

  /// <summary>
  /// Cuánto tiene que llevar escrito un documento antes de que se pueda considerar huérfano.
  /// </summary>
  /// <remarks>
  /// ⚠️ 🔴 <b>Es la única propiedad de todo el recolector que no se puede equivocar.</b> El
  /// documento se escribe <b>dentro</b> de la transacción que lo referencia, así que existe
  /// un rato antes de que exista la fila que lo apunta. Sin gracia, el recolector borraría
  /// comprobantes <b>buenos a mitad de vuelo</b> — y un comprobante borrado no vuelve.
  /// El valor por defecto está tres órdenes de magnitud por encima de esa ventana a
  /// propósito: aquí lo barato es esperar y lo caro es acertar por poco.
  /// </remarks>
  [Range(1, 720)]
  public int OrphanGraceHours { get; init; } = 24;
}
