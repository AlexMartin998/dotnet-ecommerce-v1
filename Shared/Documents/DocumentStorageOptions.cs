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
}
