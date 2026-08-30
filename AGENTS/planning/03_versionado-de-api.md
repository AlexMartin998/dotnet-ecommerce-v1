# 03 — Versionado de API + Swagger  ✅

Contrato: [`features/03_versionado-de-api.feature`](../features/03_versionado-de-api.feature) · Commit `34a3b44`

## Hecho
- [x] `Asp.Versioning.Mvc` 8.1.0 (⚠️ la 10.x es solo `net10.0`)
- [x] `UrlSegmentApiVersionReader` + `[Route("api/v{version:apiVersion}/[controller]")]`
- [x] `ReportApiVersions` (cabeceras `api-supported-versions` / `api-deprecated-versions`)
- [x] `ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>` → un documento por
      versión desde `IApiVersionDescriptionProvider`
- [x] `HttpContextExtensions.ApiVersionValue()` para `CreatedAtRoute`
- [x] `GenerateDocumentationFile` + `NoWarn 1591` para que los `///` salgan en Swagger

## Decisiones
- Segmento de URL y no query string ni cabecera: es el único que se ve en un log, se
  cachea y se comparte como enlace.
- `AssumeDefaultVersionWhenUnspecified = false`: con versionado por ruta esa opción es
  humo — `/api/category` no matchea ninguna plantilla y da 404 antes.
- Deprecar con `[ApiVersion("1.0", Deprecated = true)]`, **no** con el `[Obsolete]` de la
  BCL (ese no emite la cabecera HTTP).

## Trampas registradas
- `CreatedAtRoute` necesita el parámetro `version` explícito.
- Un controller sin `[ApiVersion]` da **404**: los de infraestructura llevan `[ApiVersionNeutral]`.
- Los `Name` de ruta deben ser únicos entre versiones (en el curso estaban duplicados).

## Abierto
- No hay v2. Cuando la haya: `[ApiVersion("2.0")]` en el controller y **cero** cambios en
  `Program.cs`.
