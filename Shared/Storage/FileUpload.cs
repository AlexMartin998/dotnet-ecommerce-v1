namespace ApiEcommerce.Shared.Storage;


/// <summary>
/// Un archivo subido, ya desacoplado de ASP.NET Core.
/// </summary>
/// <remarks>
/// Existe para que la capa de servicio <b>no reciba un <c>IFormFile</c></b>. El
/// controller adapta el tipo del framework a este record y a partir de ahí el
/// servicio (y su test) solo necesita un <see cref="Stream"/>. En el código de
/// referencia el <c>IFormFile</c> viajaba dentro de los DTOs de <c>Models/Dtos</c>,
/// acoplando el modelo al framework web.
/// </remarks>
/// <param name="Content">Contenido del archivo. Quien lo crea es dueño de cerrarlo.</param>
/// <param name="FileName">Nombre original del cliente. <b>Dato no confiable</b>: solo se usa para leer la extensión.</param>
/// <param name="ContentType">MIME declarado por el cliente. También no confiable; se verifica contra el contenido real.</param>
/// <param name="Length">Tamaño en bytes.</param>
public sealed record FileUpload(Stream Content, string FileName, string ContentType, long Length);
