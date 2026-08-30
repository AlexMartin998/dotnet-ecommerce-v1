# 06 — Estado actual y roadmap

Foto del repo al **2026-08-30**, después de traer a esta arquitectura las
features de las secciones 8–15 del curso de referencia
(`AGENTS/__ref__/01/code`): autenticación, CORS, cache, versionado, imágenes,
paginación y seeding.

## Estado por componente

| Componente | Estado | Nota |
| --- | --- | --- |
| `AppDbContext` + migraciones | ✅ | 8 migraciones aplicadas (la última, `OutboxAndProcessedMessages`); auditoría automática en `SaveChangesAsync` |
| `IEntity` / `IAuditable` | ✅ | implementadas por `Category` y `Product`; sin reflexión en los genéricos |
| `IBaseRepository<T>` / `BaseRepository<T>` | ✅ | `where T : class, IEntity`, `CancellationToken`, orden genérico por `CreatedAt` |
| `CategoryRepository` | ✅ | `NameExistsAsync(excludeId)`, `HasProductsAsync` |
| `ProductRepository` | ✅ | lecturas con `Include(Category)`, `GetBySkuAsync`, `SkuExistsAsync`, búsqueda |
| Registro DI de `IProductRepository` | ✅ | corregido |
| `ICrudService` / `CrudService` (DTO-facing) | ✅ | `sealed`, compuesto, sin reflexión |
| `IEntityRules` / `NoEntityRules` | ✅ | default interface members; reglas fuera del CRUD |
| `IGenericService<T>` / `GenericService<T>` | 🗑️ retirado | comentados como registro de aprendizaje; sin registro en DI |
| `CategoryService` + `CategoryRules` | ✅ | compone el CRUD; nombre único + no borrar con productos |
| `ProductService` + `ProductRules` | ✅ | escrituras delegadas, lecturas propias; SKU único + FK válida + compra |
| `CategoryController` | ✅ | sin `try/catch` de negocio |
| `ProductController` | ✅ | CRUD + `category/{id}` + `search` + `buy` |
| `HealthController` | ✅ | `GET /health` (liveness), `[ApiVersionNeutral]` |
| Jerarquía `AppException` | ✅ | + `Unauthorized` (401), `Forbidden` (403), `Validation` (422) |
| Handler global de errores | ✅ | `GlobalExceptionHandler` + `ProblemDetails` (RFC 7807) |
| `TransactionalAttribute` | ✅ | aplicado a `POST /api/v1/product/buy`; efectivo desde que `AddPersistence` activa `EnableRetryOnFailure` |
| AutoMapper | ✅ | `CategoryProfile`, `ProductProfile`; `MappingProfile` retirado (comentado) |
| Validación de DTOs | ✅ | DataAnnotations completas en los 5 DTOs de entrada |
| Registro de DI | ✅ | un `XExtensions.cs` por feature, en su carpeta; `Shared/DependencyInjection/` es solo composition root (`AddApplication`/`AddInfrastructure`/`AddWebApi`) |
| `CancellationToken` extremo a extremo | ✅ | controller → servicio → repositorio → EF |
| Identity + JWT | ✅ | `ApplicationUser`, `AddIdentityCore`, `JwtOptions` validado con `ValidateOnStart` |
| `AuthController` | ✅ | `register` / `login` / `me`, rol fijo `user` en el registro |
| Autorización por rol | ✅ | clase `[Authorize]` + `[AllowAnonymous]`/`[Authorize(Roles=…)]` por acción |
| API versioning | ✅ | segmento de URL `/api/v1/…`, un documento de Swagger por versión |
| CORS | ✅ | orígenes desde `Cors:AllowedOrigins`, falla cerrado |
| Cache distribuida (Redis) | ✅ | `ICacheService` + `CachedCategoryService` (decorador) |
| Paginación | ✅ | `PagedResult<T>`, `GetPagedAsync`, `/paged` en Category y Product |
| Subida de imágenes | ✅ | `IFileStorage`, allowlist + magic bytes, ruta relativa |
| Seeding | ✅ | `DataSeeder` con `RoleManager`/`UserManager`, guardado por `Seed:Enabled` |
| Rate limiting | ✅ | límite global por IP + política `auth` |
| Logging estructurado | ✅ | Serilog + `UseSerilogRequestLogging` (sección `Serilog`, no `Logging`) |
| Health checks | ✅ | `/health` liveness (controller) y `/health/ready` (SQL Server + Redis) |
| Concurrencia: stock | ✅ | `TryDecrementStockAsync` con `ExecuteUpdateAsync` (UPDATE condicional atómico) |
| Concurrencia: unicidad | ✅ | índice único en `Category.Name` + `Product.SKU`; `DbUpdateException` → 409 |
| Concurrencia optimista | ✅ | `Product.RowVersion` para el PATCH; `DbUpdateConcurrencyException` → 409 |
| Idempotencia de peticiones | ✅ | `[Idempotent]` + `Idempotency-Key` sobre Redis (`SET NX`) |
| Outbox transaccional | ✅ | `OutboxMessage` en la misma transacción que el negocio; la API funciona con el broker caído |
| Publicación a RabbitMQ | ✅ | `OutboxPublisher` (BackgroundService) con publisher confirms y mensajes persistentes |
| Consumidor | ✅ | `ProductPurchasedConsumer`: ack manual, prefetch, DLQ, dedupe por `ProcessedMessage` |
| Dockerfile + compose | ✅ | multi-stage, usuario `$APP_UID` de la imagen base, `curl` instalado para el healthcheck |
| Migraciones al arrancar | ✅ | `MigrateAsync()` en el scope de arranque (la imagen runtime no lleva `dotnet-ef`) |
| Idempotencia de la config | ✅ | validación condicional de `SeedOptions`; `ValidateOnStart` en todas las secciones |
| `UseForwardedHeaders` | ✅ | el rate limiter particiona por la IP real, no por la del proxy |
| Sonda de backlog del outbox | ✅ | `outbox-backlog` → `Degraded` si hay eventos que agotaron reintentos |
| Tests | ❌ | sin proyecto de pruebas |

## Lo que se verificó (2026-08-30)

`dotnet build` limpio (0 warnings) y smoke test contra SQL Server y Redis reales
(`sqlserver_ecommerce` en `172.17.0.1,1434`, `redis_generic` en `172.17.0.1:6999`):
liveness y readiness 200; documento de Swagger v1; catálogo anónimo; paginación
(200 con `[]` fuera de rango, 400 con `pageSize` sobre el tope); 401 sin token y
404 en la ruta sin versionar; registro 201 / 409 duplicado / 422 password débil;
login 401 con credenciales malas; `auth/me` con token; 403 de un rol `user` sobre
una escritura; `CreatedAtRoute` versionado devolviendo `Location`; PATCH parcial
que no pisa `Name`; 404 tras el DELETE; SKU duplicado 409; FK inexistente 400;
borrado de categoría con productos 409; compra por un usuario **no** admin 200 y
409 por stock insuficiente; subida de PNG válido y descarga estática 200; rechazo
de un `.txt` renombrado a `.png` y de una extensión no permitida; 429 al superar
la ventana de `auth`; y `Cache MISS` → `Cache HIT` → invalidación en el PATCH,
con la clave `apiecommerce:category:1003` verificada en Redis.

### Verificación anterior (2026-08-23)

Smoke test de 18 casos contra la base real,
cubriendo el camino feliz y **cada excepción de dominio**: 409 por nombre
duplicado, 409 por SKU duplicado, 409 por borrar categoría con productos, 409 por
stock insuficiente, 404 por id/SKU inexistente, 400 por FK inexistente, 400 de
DataAnnotations, 200 con `[]` en búsqueda sin resultados, y PATCH parcial que no
pisa los campos que no vienen.

## Roadmap

### Paso 7 — Proyecto de tests `ApiEcommerce.Tests`

Sigue siendo el siguiente paso, y ahora hay más superficie que merece cobertura.

1. `dotnet new xunit -o ../ApiEcommerce.Tests` + referencia al proyecto.
2. `CategoryRules` / `ProductRules` con un `IXRepository` mockeado (Moq): un test
   por excepción de dominio. **Empezar por aquí**.
3. `CrudService` con `IBaseRepository<T>` + `IMapper` mockeados.
4. `AuthService` con `UserManager`/`SignInManager` mockeados: 409 por duplicado,
   422 por password débil, 401 con el **mismo** mensaje para usuario inexistente y
   password incorrecta, 403 por lockout, y que el registro **nunca** asigne `admin`.
5. `LocalFileStorage`: que rechace extensión no permitida, magic bytes que no
   casan, y tamaño por encima del límite.
6. `CachedCategoryService` con un `ICacheService` falso: que la invalidación
   ocurra **después** de la escritura y **no** ocurra si el servicio interno lanza.
7. Perfiles de AutoMapper: `AssertConfigurationIsValid()` + test del PATCH parcial.

### Paso 8 — Deuda conocida de la revisión de 2026-08-30 (no cerrada)

Tres revisiones con `dotnet-best-practices` sobre concurrencia, mensajería e
infraestructura. Los P0 y los P1 baratos están corregidos; **esto es lo que se
dejó abierto a propósito**, para que nadie lo descubra creyendo que es nuevo:

- **Reintentos del consumidor sin contador real.** `args.Redelivered` es una
  bandera del broker, no un contador: son 2 intentos como máximo y sin backoff.
  Lo correcto es una *retry queue* con `x-message-ttl` que dead-letterea de vuelta
  a la principal, leyendo `x-death[0].count`. Se quitó `MaxDeliveryAttempts` de la
  configuración por no dejar una opción muerta que documenta algo que no ocurre.
- **`OutboxPublisher` sin claim: con más de una réplica publica duplicados.** El
  `SELECT` no tiene `UPDLOCK`/`READPAST` ni columna de reserva, así que dos
  instancias leen el mismo lote. No corrompe (el consumidor deduplica) pero dobla
  el tráfico. Antes de correr con varias réplicas: `sp_getapplock` o un
  `UPDATE ... OUTPUT` que reserve el lote.
- **Sin limpieza de `OutboxMessages` ni de `ProcessedMessages`.** Crecen sin
  límite; hace falta un job de purga de lo ya procesado.
- **`RowVersion` no cierra el *lost update* entre dos administradores.** El token
  no se expone en `ProductDto` ni se acepta en `UpdateProductDto`, así que el
  PATCH usa el que acaba de leer. Cerrarlo es publicarlo como `ETag` y exigir
  `If-Match`. El XML doc de `Product.RowVersion` ya dice exactamente qué garantiza
  y qué no.
- **La clave de idempotencia no incluye un hash del cuerpo.** La misma clave con
  otro payload reproduce la respuesta del primero en silencio; lo estándar
  (Stripe) es guardar el hash y devolver 422 si no coincide.
- **`AutoMapper 15.1.1` exige licencia comercial en producción** (avisa por log al
  arrancar). Es una decisión de producto pendiente: comprar licencia, fijar
  AutoMapper ≤ 13.x (última MIT), o migrar a Mapperly. Ver `05-convenciones.md`.

### Paso 9 — Partir en proyectos (cuando duela, no antes)

Los tres bloques del composition root (`AddApplication` / `AddInfrastructure` /
`AddWebApi`) son ya las costuras: `ApiEcommerce.Api` / `.Infrastructure` /
`.Application` (+ `.Domain`). Hoy la dirección de dependencias es una convención;
partir en proyectos la convierte en algo que impone el compilador. **No es
urgente**: hacerlo antes de que el proyecto lo pida solo añade fricción.

### Paso 10 — Refresh tokens y revocación

El access token dura 60 min y no se puede revocar. El claim `jti` ya se emite: con
él, una denylist en Redis (la misma instancia que ya está conectada) permite
invalidar un token concreto. Un refresh token rotatorio en base cierra el ciclo.

### Paso 11 — Endpoint de administración de usuarios

`GET /api/v1/user` (listado, admin), `POST /api/v1/user/{id}/roles` (promover a
admin). Hoy el único camino para tener un admin es el seeder.

### Paso 12 — Deudas conocidas y anotadas

- **`DateTime.Now` → `DateTimeOffset`/UTC.** Ya se nota la inconsistencia: los
  `exp`/`nbf` del JWT van en UTC (lo exige la RFC 7519) mientras el resto del
  modelo usa hora local, y `UpdatedAt` recién estampado se serializa con offset
  mientras el leído de base sale sin él. Es una migración de todo a la vez:
  entidades, DTOs y datos.
- **Listados sin paginar.** `GET /api/v1/category` y `GET /api/v1/product` siguen
  trayendo la tabla entera; existen `/paged` al lado. El siguiente paso es
  deprecarlos por versión (`v2` sin ellos), no borrarlos, que sería breaking.
- **Cache de listados paginados.** Hoy no se cachean: exigiría invalidar por
  prefijo o versionar la clave de colección.
- **Almacenamiento de imágenes en disco local.** No escala horizontalmente ni
  sobrevive al redespliegue de un contenedor. `IFileStorage` existe para que el
  cambio a blob storage sea de una clase.
- **`UseForwardedHeaders`.** El rate limiting particiona por IP remota; detrás de
  un proxy hará falta activarlo o todos los clientes compartirán partición.
- **Rotación de `Jwt:SecretKey` en caliente.** `JwtTokenService` es singleton y
  materializa las `SigningCredentials` en el constructor, así que rotar la clave
  exige reiniciar. Se resuelve cambiando `IOptions` por `IOptionsMonitor`.

## Cómo mantener este documento

Al terminar un paso, actualizar la tabla de estado en el mismo commit que el
código. Un roadmap desactualizado es peor que no tenerlo: hace que quien lo lee
(persona o agente) trabaje sobre una foto falsa del repo.
