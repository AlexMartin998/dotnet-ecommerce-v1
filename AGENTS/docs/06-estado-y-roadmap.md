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
| Reintentos del outbox | ✅ | **un broker caído no consume intentos**: solo los cuenta el fallo atribuible a un mensaje (`BrokerUnavailableException`) |
| Consumidor | ✅ | `ProductPurchasedConsumer`: ack manual, prefetch, DLQ, dedupe por `ProcessedMessage` |
| Dockerfile + compose | ✅ | multi-stage, usuario `$APP_UID` de la imagen base, `curl` instalado para el healthcheck |
| Migraciones al arrancar | ✅ | `MigrateAsync()` en el scope de arranque (la imagen runtime no lleva `dotnet-ef`) |
| Idempotencia de la config | ✅ | validación condicional de `SeedOptions`; `ValidateOnStart` en todas las secciones |
| `UseForwardedHeaders` | ✅ | el rate limiter particiona por la IP real, no por la del proxy |
| Sonda de backlog del outbox | ✅ | `outbox-backlog` → `Degraded` si hay eventos que agotaron reintentos; el umbral sale de `RabbitMq:MaxPublishAttempts`, el mismo que aplica el publicador |
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

**Unitarios hechos** (2026-09-06, 105 tests): reglas de dominio, `CrudService`,
`AuthService`, `LocalFileStorage`, `CachedCategoryService`, `PagedResult`, los
perfiles de AutoMapper y `GlobalExceptionHandler`. Viven en `tests/ApiEcommerce.Tests`
(dentro del repo, con `tests/**` excluido del `.csproj` de la API) y usan xunit +
Moq, **sin FluentAssertions** — desde la v8 exige licencia comercial.

**Integración, concurrencia y degradación hechas** (2026-09-06, 153 tests en
total). Levantan la API entera con `WebApplicationFactory` contra SQL Server y Redis
reales, con base (`ApiEcommerceNET8_Tests`) y prefijo de Redis propios.
⚠️ **Sin Testcontainers**: no hay Docker en el dev container. Entra en CI.

Cubren la matriz de autorización de `features/02`, el versionado, la paginación, la
idempotencia, las **tres carreras** con `Task.WhenAll` (que secuencialmente pasaban
también con la implementación defectuosa), la degradación con Redis caído y el
arranque en `Production` y sin clave de firma.

**CI hecha**: `.github/workflows/ci.yml` corre build con `-warnaserror` y los 153
tests en cada push y PR, con SQL Server y Redis como `services` del runner, y
construye el `Dockerfile` (que nunca se había construido). ⚠️ Queda que el owner lo
suba y active la protección de rama: el agente no hace `push`.

Migrar la integración a Testcontainers sigue siendo una opción —en el runner sí hay
Docker— pero ya no es necesaria: los `services` dan lo mismo con menos piezas.

⚠️ **Un test que pasa contra el código roto no vale nada.** La suite unitaria se
validó por mutación: reintroducidos dos bugs reales ya corregidos (el `MapFrom`
explícito de `CategoryId` y el `FindSqlException` que solo miraba un nivel), cayeron
exactamente los tests que debían. Conviene repetir el ejercicio con cada bloque nuevo.

⚠️ Y no es teórico: los primeros tests de idempotencia **pasaban sin probar nada**
porque el host de tests arrancaba con `NoIdempotencyStore`. La causa es que las
piezas que leen configuración de forma *eager* para decidir **qué implementación
registran** no ven lo que inyecta `ConfigureAppConfiguration` — hay que usar
`UseSetting`. Es el precio del patrón que este documento bendice en otro sitio, y
conviene tenerlo presente al añadir otra decisión de registro por configuración.

### Paso 8 — Deuda de la revisión de 2026-08-30 — ✅ **cerrada** (2026-09-06)

Todo lo que se dejó abierto a propósito está hecho. Lo que se decidió y por qué:

| Deuda | Cómo se cerró |
|---|---|
| Reintentos del consumidor sin contador real | Cola de espera con `x-message-ttl` que dead-letterea de vuelta; el contador sale de `x-death[].count`. **Topología nueva**, sin tocar la cola principal: redeclararla con otros argumentos da **406** y obligaría a borrarla en producción |
| `OutboxPublisher` sin claim | **`sp_getapplock` exclusivo**, no un claim por filas: el claim deja drenar en paralelo y eso destruye la garantía de orden. Serializar un trabajo de fondo con lote acotado no cuesta nada |
| Orden del outbox por reloj | Columna `Sequence` (`bigint IDENTITY`): un único árbitro y desempate real |
| Sin purga | `OutboxCleaner`, retención configurable, borrado en tandas. **Nunca toca lo no procesado** |
| `RowVersion` no cierra el *lost update* | `ETag` en el GET + `If-Match` en el PATCH → **412**. Opcional, para no romper a los clientes actuales |
| Idempotencia sin hash del cuerpo | SHA-256 de los argumentos enlazados, guardado **desde la reserva**; clave reutilizada con otro cuerpo → **422** |
| Sin OpenTelemetry | Trazas y métricas + `X-Correlation-Id`. **Se instrumenta siempre, se exporta solo si hay endpoint** |
| Sin CI | `.github/workflows/ci.yml`, con `-warnaserror` y un job que construye la imagen |
| `UseHsts()` | Añadido fuera de Development |
| Tres conexiones a Redis | Un solo multiplexer para la cache, el store de idempotencia y el health check |

**Lo único que queda es una decisión de producto**, no técnica: la **licencia de
AutoMapper** (la 15.1.1 exige licencia comercial en producción y avisa por log).
Opciones: comprar, fijar ≤13.x (última MIT), o migrar a Mapperly. Afecta a
`05-convenciones.md`.

**Deuda nueva que abrió este trabajo**, anotada para no descubrirla creyéndola vieja:
- El *replay* de idempotencia re-serializa el cuerpo memorizado y no es idéntico **byte
  a byte** al que escribe MVC. Equivalente para cualquier cliente que parsee JSON;
  garantizarlo exigiría capturar lo que MVC escribe de verdad.
- El publicador mantiene una transacción abierta mientras publica al broker. El lote está
  acotado, pero conviene medirlo si el lote crece.

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
