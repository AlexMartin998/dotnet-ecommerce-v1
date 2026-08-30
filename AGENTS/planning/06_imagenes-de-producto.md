# 06 — Imágenes de producto  ✅

Contrato: [`features/06_imagenes-de-producto.feature`](../features/06_imagenes-de-producto.feature) · Commit `34a3b44`

## Hecho
- [x] `Shared/Storage/`: `IFileStorage`, `LocalFileStorage`, `FileUpload`, `FileStorageOptions`
- [x] `POST /api/v1/product/{id}/image` con `[Consumes("multipart/form-data")]` y `[RequestSizeLimit]`
- [x] Validación en tres niveles: tamaño → allowlist de extensión → **magic bytes**
- [x] Nombre generado por el servidor (GUID) + `Path.GetFullPath` acotado a la carpeta
- [x] Borrado de la imagen anterior y de la del producto eliminado
- [x] `app.UseStaticFiles()` **después** de CORS y del rate limiter

## Decisiones
- **Endpoint aparte, no un campo del PATCH**: el curso cambió `POST/PUT` de `[FromBody]` a
  `[FromForm]` y rompió a todos sus clientes JSON con un breaking change no versionado.
- **`FileUpload` (record) desacopla el servicio de `IFormFile`**: el curso metía `IFormFile`
  dentro de los DTOs de `Models/Dtos`, acoplando el modelo al framework web.
- **Ruta relativa persistida, no URL absoluta**: el curso guardaba
  `{Request.Scheme}://{Request.Host}/...` y `Host` lo controla el cliente.
- **Allowlist, no denylist**: los archivos se sirven desde el mismo origen que la API, así
  que un `.svg` o `.html` sería XSS almacenado.
- `IWebHostEnvironment.WebRootPath`, no `Directory.GetCurrentDirectory()`.

## Abierto
- **Disco local no escala horizontalmente** ni sobrevive al redespliegue de un contenedor.
  `IFileStorage` existe para que el cambio a S3/Blob sea de una clase.
- Sin redimensionado ni miniaturas.
- Sin límite del número de imágenes por producto (hoy es una y se reemplaza).
