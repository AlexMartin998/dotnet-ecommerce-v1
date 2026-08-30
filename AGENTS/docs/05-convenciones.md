# 05 — Convenciones

Reglas de estilo y mecánica del día a día. Ante la duda, **imita el slice de
Category**, que es el de referencia.

## C# y estilo

- **Namespaces file-scoped**: `namespace ApiEcommerce.Repository;`
- **Namespace = carpeta**, raíz `ApiEcommerce`, carpetas de capa en singular
  (`Service`, `Repository`, `Controllers` es la excepción heredada de la plantilla).
- **Indentación: 2 espacios** en todo salvo `Controllers/`, que usa 4 (herencia
  del scaffold `dotnet new webapi`). No unificar ahora; sí mantener la coherencia
  dentro de cada archivo.
- `Nullable` e `ImplicitUsings` están **enabled**. No se agregan `using System;`
  ni se apagan las advertencias de nullabilidad: si el compilador se queja de un
  `null`, el diseño tiene un hueco real.
- **Constructor primario** para repositorios y para las clases de reglas:
  `public class CategoryRepository(AppDbContext db) : BaseRepository<Category>(db), ICategoryRepository`,
  `public sealed class CategoryRules(ICategoryRepository repository) : IEntityRules<...>`.
  **Constructor clásico** para servicios y controllers (más legible cuando hay
  varias dependencias y campos `_readonly`). Ambos estilos conviven a propósito.
- **`CancellationToken ct = default` como último parámetro** de toda operación
  async, propagado hasta EF Core. En los controllers entra como parámetro de la
  acción (`CancellationToken ct`) y ASP.NET lo enlaza solo. Un request abortado
  deja de consumir la base, y el handler global lo traduce a 499.
- `async`/`await` en **todo** lo que toque la base. Nada de `.Result` ni
  `.Wait()`. Los métodos async terminan en `Async`.
- Un método que solo reenvía una `Task` puede devolverla sin `await`
  (`public Task<T?> GetByIdAsync(int id) => _repository.GetByIdAsync(id);`).
- **Comentarios en español**, como el resto del repo y `notes.md`.
- Los **bloques comentados de implementaciones anteriores se conservan**: son
  registro de aprendizaje del autor. No borrarlos salvo petición explícita.

## Documentación XML

Comentarios `///` en lo público **no obvio**: interfaces (`IBaseService`,
`IBaseRepository`), miembros abstractos (`EntityName`), hooks (`OnBeforeCreateAsync`)
y cualquier método cuya semántica no se deduzca del nombre (`ExistsByFieldAsync`).
Un `/// <summary>Gets the id.</summary>` sobre `GetId` es ruido: no se agrega.

## Controllers

- `[ApiController]` + `[ApiVersion("1.0")]` + `[Route("api/v{version:apiVersion}/[controller]")]`.
  El segmento final sale del prefijo de la clase: `CategoryController` → `/api/v1/category`.
  Un controller de infraestructura que no forma parte del contrato versionado
  (`HealthController`) lleva `[ApiVersionNeutral]`; **sin él da 404**, porque el
  versionador exige una versión que su ruta no tiene.
- `CreatedAtRoute` sobre una ruta versionada **debe pasar el parámetro `version`**:
  `CreatedAtRoute("GetCategory", new { version = HttpContext.ApiVersionValue(), id = newId }, null)`.
- **Rutas con nombre** en todas las acciones (`[HttpGet("{id:int}", Name = "GetCategory")]`)
  para que `CreatedAtRoute` pueda referenciarlas.
- **Constraints de ruta** siempre que apliquen: `{id:int}`. Evita entrar al
  action con basura y da un 404 de routing gratis.
- `[ProducesResponseType]` en **todas** las acciones, con todos los códigos que
  el endpoint puede producir de verdad (ver regla 6 de `04-error-handling.md`).
- **`PATCH`, no `PUT`**, para actualizaciones — el update es parcial y mapea el
  DTO sobre la entidad rastreada.
- Verbos y respuestas del camino feliz:

| Acción | Verbo | Éxito | Body |
| --- | --- | --- | --- |
| listar | `GET /api/x` | 200 | `IEnumerable<XDto>` (vacío si no hay) |
| obtener | `GET /api/x/{id:int}` | 200 | `XDto` |
| crear | `POST /api/x` | 201 | `CreatedAtRoute`, header `Location` |
| actualizar | `PATCH /api/x/{id:int}` | 204 | vacío |
| borrar | `DELETE /api/x/{id:int}` | 204 | vacío |

## Autorización

- El controller lleva **`[Authorize]` a nivel de clase** (el requisito más débil:
  estar autenticado); las lecturas públicas se abren con `[AllowAnonymous]` y las
  escrituras se restringen con `[Authorize(Roles = Roles.Admin)]` acción a acción.
- **Varios `[Authorize]` se combinan (AND), no se sobreescriben.** Poner
  `[Authorize(Roles = "admin")]` en la clase y `[Authorize]` en una acción **no
  relaja nada**: la acción sigue exigiendo `admin`. El único atributo que gana
  sobre la clase es `[AllowAnonymous]`. Por eso el requisito fuerte va en la
  acción y no en la clase.
- Cerrar por defecto y abrir a mano: si se añade un endpoint y se olvida el
  atributo, queda protegido, no público.
- Los nombres de rol son `const string` en `Shared/Auth/Roles.cs`; `[Authorize]`
  es un atributo y solo admite constantes de compilación.

## Configuración

- Toda sección de configuración se enlaza a una **clase tipada con
  DataAnnotations** (`JwtOptions`, `CacheOptions`, `SeedOptions`,
  `FileStorageOptions`) mediante
  `AddOptions<T>().Bind(...).ValidateDataAnnotations()`. Es el
  `@ConfigurationProperties` + `@Validated` de Spring.
- Lo que sin ello rompería en producción lleva además **`.ValidateOnStart()`**
  (hoy, `JwtOptions`): un secreto ausente tumba el arranque, no el primer login.
- **Los secretos no se commitean.** `appsettings.json` lleva los valores vacíos;
  `appsettings.Development.json` lleva los de desarrollo; el resto va en
  user-secrets (`dotnet user-secrets set "Jwt:SecretKey" "…"`) o en variables de
  entorno (`Jwt__SecretKey`, doble guion bajo por cada `:`).
- **Serilog lee la sección `Serilog`, no la sección `Logging`** del scaffold. Se
  eliminó `Logging` de `appsettings.json` para no tener dos fuentes de verdad de
  las que solo una funciona.

## DTOs y validación

- Un DTO por propósito: `XDto` (salida), `CreateXDto` (POST), `UpdateXDto` (PATCH).
- **La validación de forma vive en el DTO**, con DataAnnotations:
  `[Required]`, `[MaxLength]`, `[MinLength]`, `[Range]`, `[RegularExpression]`,
  siempre con `ErrorMessage` explícito.
- En `CreateXDto` los campos obligatorios llevan `[Required]`; en `UpdateXDto`
  todo es nullable/opcional.
- En `UpdateXDto` **todo es nullable, tipos valor incluidos** (`decimal? Price`,
  `int? Stock`, `int? CategoryId`). Ver `01-capas-y-contratos.md` → Models/Dtos.
- Semántica del PATCH: **omitir un campo (o enviarlo `null`) significa "no
  tocar"**. Para vaciar un campo opcional se envía `""`, no `null`.

## Mapping

- Un `Profile` por entidad: `Mapping/XProfile.cs`. El escaneo de assembly en
  `Program.cs` los registra solos.
- `MappingProfile.cs` está **retirado** (comentado como registro de aprendizaje):
  duplicaba los mapas de `CategoryProfile`. No reactivarlo.
- Update siempre con `_mapper.Map(dto, existing)` sobre la entidad rastreada.
- **PATCH parcial: `MapFrom((s, d) => s.X ?? d.X)` campo a campo.** No usar
  `.ForAllMembers(o => o.Condition((_, _, m) => m is not null))`: la `Condition`
  recibe el valor **ya convertido al tipo del destino**, así que un `int?` nulo
  le llega como `0`, no lo salta, y el PATCH machaca el campo con un cero. Con
  `CategoryId` eso rompe la clave foránea y devuelve un 500.
- **`CreatedAt` / `UpdatedAt` van siempre `Ignore()`** en los mapeos de escritura:
  los estampa `AppDbContext.SaveChangesAsync`.
- `Product.Category` (navegación) es `Category?` y va `Ignore()` en escritura: se
  trabaja solo con `CategoryId`. Dejarla `required` impedía que AutoMapper
  construyera un `Product` desde `CreateProductDto`. La relación sigue siendo
  obligatoria en la base porque `CategoryId` es `int` no-nullable.
- Si un `XDto` expone un campo plano de una navegación (`ProductDto.CategoryName`),
  **alguien tiene que cargar la navegación**: el repositorio expone un método con
  `.Include(...)` y el servicio lo usa en vez del `GetAllAsync` genérico. Si no,
  el campo sale nulo siempre y en silencio.

## Inyección de dependencias

Todo se registra en `Shared/DependencyInjection/ServiceCollectionExtensions.cs`,
agrupado por capa y con lifetime **Scoped**. `Program.cs` solo encadena los
bloques, así se lee como un índice:

```csharp
builder.Services.AddPersistence(builder.Configuration);   // EF Core + SQL Server
builder.Services.AddObjectMapping();                      // AutoMapper
builder.Services.AddRepositories();                       // IBaseRepository<> + repos
builder.Services.AddApplicationServices();                // CrudService + reglas + servicios
builder.Services.AddErrorHandling();                      // GlobalExceptionHandler + ProblemDetails
```

Dentro de `AddApplicationServices()`, por cada entidad:

```csharp
// reglas: genérico abierto = "sin reglas"; el registro cerrado gana
services.AddScoped(typeof(IEntityRules<,,>), typeof(NoEntityRules<,,>));
services.AddScoped<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>, CategoryRules>();

// CRUD compuesto: cerrado, porque ICrudService no lleva TEntity
services.AddScoped<ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>,
                   CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>>();

// servicio de entidad
services.AddScoped<ICategoryService, CategoryService>();
```

- **Scoped** por defecto (misma vida que `AppDbContext` y que el request).
- **Singleton** solo para cosas sin estado y sin `DbContext` (configuración,
  clientes HTTP tipados).
- El registro open-generic `IBaseRepository<>` permite inyectar
  `IBaseRepository<Product>` sin escribir un repositorio específico —
  útil para entidades sin consultas propias.
- Con genéricos, **el contenedor prefiere siempre el registro cerrado sobre el
  abierto**, sin depender del orden en que se registren. Es lo que hace que
  `CategoryRules` gane a `NoEntityRules<,,>`.

## Base de datos y migraciones

- `AppDbContext` en `Data/`, un `DbSet<T>` por entidad.
- Configuración de esquema por DataAnnotations en la entidad
  (`[Index]`, `[Column(TypeName = "decimal(18,2)")]`, `[ForeignKey]`).
  Si algo no se puede expresar así, se usa `OnModelCreating` con
  `IEntityTypeConfiguration<T>`, no se ensucia la entidad.
- Nombres de migración en **PascalCase descriptivo**:
  `CreateTableProduct`, `DescriptionInCategory`.
- Ciclo: `dotnet ef migrations add <Nombre>` → revisar el `.cs` generado
  → `dotnet ef database update`. **Revisar siempre** el archivo generado antes de
  aplicarlo; EF a veces propone un drop/recreate que pierde datos.
- `dotnet ef migrations remove` solo deshace la última migración **no aplicada**.
- La cadena `ConnectionStrings:ConexionSql` está hardcodeada en `appsettings.json`
  apuntando a un contenedor SQL Server en `172.17.0.1,1434`. Es aceptable para
  este proyecto de aprendizaje, pero **la contraseña no debería viajar en el
  repo**: al agregar JWT, mover credenciales a user-secrets
  (`dotnet user-secrets set`) o variables de entorno.

## Verificación

No hay proyecto de tests ni linter: **`dotnet build` es el único check**.
Correrlo tras cada cambio estructural, y `dotnet watch run --urls "http://0.0.0.0:8021"`
para probar contra Swagger (`/swagger/index.html`, solo en `Development`).

Cuando se agreguen tests, el destino es un proyecto hermano `ApiEcommerce.Tests`
con xUnit o MSTest + Moq sobre las interfaces (`IXRepository`), patrón AAA
(Arrange-Act-Assert), cubriendo camino feliz y cada excepción de dominio.
**Empezar por las clases `XRules`**: son las que concentran la lógica, dependen
solo de un repositorio y se instancian con un mock en una línea. Eso es
exactamente lo que se ganó al componer el CRUD en vez de heredarlo.
