namespace ApiEcommerce.Shared.Storage;


/// <summary>
/// Un archivo subido, ya desacoplado de ASP.NET Core.
/// </summary>
/// <remarks>
/// Existe para que la capa de servicio no reciba un <c>IFormFile</c>: el controller adapta
/// el tipo del framework y el servicio solo necesita un <see cref="Stream"/>.
/// </remarks>
/// <param name="Content">Contenido del archivo. Quien lo crea es dueño de cerrarlo.</param>
/// <param name="FileName">Nombre original del cliente. Dato no confiable: solo se usa para leer la extensión.</param>
/// <param name="ContentType">MIME declarado por el cliente. También no confiable; se verifica contra el contenido real.</param>
/// <param name="Length">Tamaño en bytes.</param>
public sealed record FileUpload(Stream Content, string FileName, string ContentType, long Length);
