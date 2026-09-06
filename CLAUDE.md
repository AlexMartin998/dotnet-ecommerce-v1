# CLAUDE.md

Guía para Claude Code (claude.ai/code) y cualquier otro agente que trabaje en este
repositorio.

> **Este archivo se carga en TODA sesión.** Por eso solo contiene cosas que son verdad hoy
> y que cambian poco. Lo volátil —qué se hizo la última sesión, qué toca ahora, decisiones
> pendientes del owner— vive en `AGENTS/memory.md`, y **ahí es donde hay que empezar** cada
> sesión. Una afirmación falsa aquí contamina todas las sesiones a la vez.

---

## 1. Qué es esto

**`ApiEcommerce`** — API REST de e-commerce en **ASP.NET Core 9 + EF Core 9 + SQL Server**,
con **Redis** (cache, idempotencia, denylist) y **RabbitMQ** (eventos de dominio).
Backend-only: no hay ni habrá frontend, ni vistas MVC, ni Razor Pages.

Es un **proyecto de aprendizaje con estándar de producción**. El autor viene de
**Spring Boot / Java** y usa el repo para aprender .NET replicando una arquitectura que ya
domina. Eso cambia cómo se escribe el código: **el porqué de cada decisión importa tanto
como la decisión**, y los comentarios registran la alternativa descartada, no lo que hace
la línea de al lado.

Objetivo declarado del owner: dejarlo *prod-ready para apps medianas* **sobre esta
arquitectura**, antes de saltar a Clean Architecture, DDD táctico, hexagonal o CQRS.

**Idioma** (`rules.md` §1): comentarios, XML docs, `AGENTS/**`, `notes.md` y este archivo en
**español**; identificadores, rutas y nombres de tabla en **inglés**; mensajes de excepción
de dominio en inglés (viajan al cliente); mensajes de commit en español.

## 2. Dónde vive la verdad

| Archivo | Qué contiene | Cuándo leerlo |
|---|---|---|
| `AGENTS/memory.md` | punto de continuación, trampas, decisiones que esperan al owner | **primero, siempre** |
| `AGENTS/rules.md` | las reglas duras, en su forma larga y con su porqué | antes de tocar código |
| `AGENTS/docs/` | lineamientos de arquitectura (**mandan** sobre el código) | según la tarea; índice en `docs/README.md` |
| `AGENTS/progress.md` | bitácora: qué se hizo, cómo se verificó | para saber por qué algo está así |
| `AGENTS/planning/NN_*.md` | el checklist técnico de cada tarea, con lo que quedó abierto | al retomar una tarea |
| `AGENTS/features/NN_*.feature` | el contrato Gherkin de cada tarea | al escribir tests |
| `notes.md` | el log de aprendizaje del autor, un capítulo por feature | para el contexto histórico |
| este archivo | orientación + reglas esenciales + trampas | ya lo estás leyendo |

**Precedencia:** `AGENTS/docs/` > este archivo > el código. Cuando el código discrepa de
`docs/`, se migra el código y `docs/06-estado-y-roadmap.md` registra la brecha.
⚠️ Hoy `docs/01`–`05` van por detrás del código en rutas y nombres: su **razonamiento** es
válido, sus **rutas y nombres de método** hay que contrastarlos con el código.

**Lo que NO va en este archivo**, para no tener dos fuentes de verdad: conteos que cambian
cada semana (tests, migraciones), estado de avance, y lo que ya está en `rules.md` en su
forma larga.

## 3. Cómo se levanta

```sh
dotnet restore
dotnet build                                          # API + tests
dotnet test tests/ApiEcommerce.Tests                  # necesita SQL Server y Redis arriba
dotnet watch run --urls "http://0.0.0.0:8021"         # desarrollo
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
```

- **Swagger** (solo en Development): `http://localhost:8021/swagger/index.html`, un
  documento por versión de API.
- **Sondas**: `GET /health` (liveness, no toca dependencias) y `GET /health/ready`
  (SQL Server + Redis + backlog del outbox).
- Sin `--urls`, `Properties/launchSettings.json` sirve en **5241** (http) y **7223** (https).
  En contenedor escucha en **8080** (`ASPNETCORE_HTTP_PORTS` de la imagen base).
- La solución tiene **dos proyectos**: la API y `tests/ApiEcommerce.Tests`.

⚠️ **La app NO arranca sin user-secrets.** El repo no lleva ningún secreto, tampoco los de
desarrollo. Tras clonar hay que poner `ConnectionStrings:ConexionSql`, `Jwt:SecretKey` y
`Seed:AdminPassword`; los comandos exactos están en `README_init.md`. Sin la clave JWT el
arranque falla con `OptionsValidationException`, que es lo correcto.

⚠️ **Arrancar y matar el proceso**: `dotnet run` deja un hijo que no muere con el padre; usa
`dotnet bin/Debug/net9.0/ApiEcommerce.dll`, que sí da el PID real. Y **nunca**
`pkill -f ApiEcommerce`: el cwd contiene esa cadena y el comando se mata a sí mismo (ya ha
pasado dos veces).

### Infraestructura — vive FUERA de este repo

En un `docker-compose` central del autor (`~/Documents/code/000_infra`), compartido con
otros proyectos. Este repo solo aporta `docker-compose.fragment.yml` (bloques para pegar
allí) y su propio `Dockerfile` + `docker-compose.prod.yml`.

| Servicio | Si está caído |
|---|---|
| **SQL Server** (BD `ApiEcommerceNET8`) | la API no hace nada útil: es la fuente de verdad |
| **Redis** | la app **arranca igual**; cache, atajo de idempotencia y denylist de `jti` degradan **en abierto** |
| **RabbitMQ** | la app **arranca igual**; los eventos se acumulan en `OutboxMessages` hasta que vuelva |

⚠️ Dos direcciones valen para lo mismo: la IP LAN del host (en
`appsettings.Development.json`, **cambia con DHCP**) y `172.17.0.1`, la puerta del bridge de
Docker, estable desde el dev container y lo que usan los tests por defecto.
⚠️ El Redis es **compartido con otros proyectos**: si alguien le pone `allkeys-lru`, las
claves del atajo de idempotencia se desalojarían. La garantía no depende de eso (§7.2).
⚠️ **Dentro del dev container no hay Docker**: ni el `Dockerfile` ni los composes se pueden
construir desde aquí; los ejecuta el owner en el host.

### Configuración

Cada sección se enlaza a una **clase de options tipada** validada con
`AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`: `JwtOptions`,
`RefreshTokenOptions`, `CacheOptions`, `IdempotencyOptions`, `OutboxOptions`,
`RabbitMqOptions`, `FileStorageOptions`, `DocumentStorageOptions`, `RateLimitOptions`,
`ObservabilityOptions`, `SeedOptions`.

- Los servicios reciben **`IOptions<T>`, nunca `IConfiguration`**. Para configurar opciones
  *del framework* desde las nuestras: `IConfigureOptions<T>` / `IConfigureNamedOptions<T>`
  (`ConfigureJwtBearerOptions`, `ConfigureSwaggerOptions`).
- Leer configuración **eager, en el registro**, solo vale para decidir **qué
  implementación** se registra (`AddDistributedCaching`, `AddMessaging`,
  `AddDocumentStorage`, `AddHealthProbes`).
- ⚠️ Una regla **condicional** no se expresa con `[Required]`: se evalúa siempre que alguien
  lea `.Value`, y con el seeding apagado eso era un crash-loop. Va en `.Validate(...)`.
- ⚠️ Los **arrays de configuración se FUSIONAN por índice**, no se reemplazan: las listas
  dependientes del entorno (`Cors:AllowedOrigins`) van **vacías** en el fichero base.
- **Serilog lee la sección `Serilog`**, no `Logging`. La del scaffold se quitó a propósito
  para no tener dos fuentes de verdad.

## 4. Mapa del repo

```
Features/                 <- un contexto acotado por carpeta (vertical slicing)
  Catalog/                categorias y productos          <- SLICE DE REFERENCIA
    Models/ Dtos/ Repository/ Service/ Mapping/ Controllers/
    Events/               ProductPurchased
    Messaging/            ProductPurchasedConsumer + IProductPurchasedHandler (el EFECTO)
    CatalogExtensions.cs  AddCatalogFeature(): el DI del slice
  Accounts/               identidad, JWT, sesiones, administracion de usuarios
    Models/ Dtos/ Repository/ Service/ Controllers/
    JwtOptions.cs RefreshTokenOptions.cs RefreshTokenCookie.cs
    ConfigureJwtBearerOptions.cs RefreshTokenCleaner.cs AccountsExtensions.cs
  Ordering/               ordenes y su comprobante en PDF
    Models/ Dtos/ Repository/ Service/ Controllers/
    Ports/                ICatalogGateway: LO UNICO del slice que conoce Catalog
    Events/               OrderPlaced
    Messaging/            OrderPlacedConsumer + IReceiptGenerator (el EFECTO)
    Documents/            QuestPdfReceiptRenderer (la libreria de PDF, aislada)
    OrderingExtensions.cs
Shared/                   <- transversal, de ningun dominio
  Persistence/            IEntity, IAuditable, IBaseRepository, BaseRepository, PersistenceExtensions
  Crud/                   ICrudService, CrudService, IEntityRules, NoEntityRules
  Db/                     ITransactionRunner, TransactionRunner, TransactionalAttribute (sin uso)
  Idempotency/            CommandIntent, ICommandLog, IIdempotencyStore, IdempotentAttribute
  Messaging/              outbox, inbox, RabbitMq/ (EventConsumer<,>, EventSubscription...)
  Caching/                ICacheService, RedisCacheService, NoCacheService, CacheKeys
  Storage/                IFileStorage    -> imagenes PUBLICAS (dentro de wwwroot)
  Documents/              IDocumentStore  -> documentos PRIVADOS (fuera de wwwroot)
  Auth/                   Roles, SeedOptions, IAccessTokenDenylist, extensiones de ClaimsPrincipal
  Paging/ Mapping/ Observability/
  Http/                   GlobalExceptionHandler, CORS, rate limit, versionado+Swagger, Health/
  DependencyInjection/    composition root — NO REGISTRA NADA
Data/                     AppDbContext + DataSeeder
Exceptions/               jerarquia AppException (dominio -> HTTP)
Migrations/               EF Core
tests/ApiEcommerce.Tests/ xunit + Moq, estructura espejo del slicing
AGENTS/                   docs/ features/ planning/ + memory.md progress.md rules.md
```

**Namespaces = ruta de carpeta**, file-scoped: `ApiEcommerce.Features.Catalog.Repository`,
`ApiEcommerce.Shared.Messaging.RabbitMq`.

⚠️ **`AGENTS/**` y `tests/**` están excluidos de la compilación** en el `.csproj`. Sin eso,
el glob implícito del SDK Web los compila dentro de la API (tipos duplicados, y xunit en la
imagen de producción). **No quitar esas exclusiones.**

## 5. Arquitectura

### 5.1 El flujo, y las capas que no se filtran

**Controller → Service (DTO in/out) → Repository (entidad in/out) → `AppDbContext`.**

| Capa | Entra | Sale | Prohibido |
|---|---|---|---|
| Controller | DTO, ruta, query | `IActionResult` con DTOs | `try/catch` de negocio, `IMapper`, repositorios, LINQ, entidades |
| Service | DTOs | DTOs | devolver entidades, conocer `HttpContext`/`StatusCodes`, usar `AppDbContext` |
| Repository | entidades, ids | entidades, `bool`, colecciones | conocer DTOs o `IMapper`, lanzar excepciones de negocio, aplicar reglas |

- **El repositorio devuelve `null`/`false`/vacío**; quien decide que «no encontrado» es un
  404 es el servicio.
- **El servicio lanza `AppException`**; quien la traduce a HTTP es `GlobalExceptionHandler`.

### 5.2 Un slice es un contexto acotado, NO una entidad

`UnitOfMeasurement`, `ProductTag` o `Brand` **no crean un slice**: son vocabulario del
catálogo y viven dentro de `Catalog/`. La pregunta es de DDD: *¿esto tiene su propio
lenguaje ubicuo y sus propias invariantes, o es parte del vocabulario de otro contexto?*
Un slice por entidad reproduce la dispersión que el slicing venía a quitar, con más carpetas.

**Añadir un slice = crear su carpeta y una línea en `AddFeatures()`.** Contextos previstos:
`Catalog`, `Accounts`, `Ordering` (los tres existen), `Payments`, `Shipping`.

⚠️ **`Shared/` no nombra tipos de `Features/`.** La excepción legítima es el composition
root, que por definición conoce ambos lados (por eso el ensamblado que escanea AutoMapper se
le pasa desde allí). Si algo de `Shared/` necesita un tipo de un slice, o el tipo está en el
sitio equivocado, o hay que **invertir** la dependencia.
⚠️ Hay **una segunda, y es una trampa**: `Shared/Mapping/MappingProfile.cs` conserva tres
`using` de `Catalog` vivos porque su cuerpo es un bloque comentado de aprendizaje. **No lo
"limpies"**: §9 prohíbe borrar esos bloques, y quitar los `using` los rompería.

⚠️ **Un slice NO habla con los tipos de otro: habla con un puerto suyo.** `Ordering` no
conoce `IProductRepository`; conoce `ICatalogGateway`, y toda la dependencia cruzada cabe
en **una** clase adaptadora. Es lo que hace que un slice se pueda mover.

### 5.3 La regla que lo sostiene todo

> **Se hereda para reutilizar MECANISMO, se compone para reutilizar POLÍTICA.**

- **Repositorio → herencia.** `BaseRepository<T>` (`where T : class, IEntity`) es CRUD sobre
  `DbSet<T>`. Sus métodos **no son `virtual`** a propósito: la base es mecanismo puro sin
  decisiones de negocio, y las subclases solo **añaden** consultas de dominio, nunca alteran
  en silencio la semántica documentada en `IBaseRepository`.
- **Servicio → composición.** `ICrudService<TDto,TCreateDto,TUpdateDto>` es DTO-facing y
  **deliberadamente no lleva `TEntity`**, para que un controller no pueda ni ver la entidad.
  `CrudService<TEntity,…>` es `sealed`: sus invariantes no se sobreescriben. Los servicios
  de entidad **tienen** un `CrudService` y le delegan; no heredan.
- **Las reglas de negocio viven fuera del servicio**, en
  `IEntityRules<TEntity,TCreateDto,TUpdateDto>`, con *default interface members* para que
  una entidad implemente solo lo que necesita. `NoEntityRules<,,>` se registra como genérico
  abierto por defecto; los registros **cerrados** por entidad ganan sobre él.
- Un servicio puede **saltarse la delegación** para una operación concreta (`ProductService`
  hace sus propias lecturas porque necesitan `Include(Category)`), y un slice puede **no
  usar el CRUD genérico en absoluto** (`Ordering`: una orden no se actualiza ni se borra, se
  coloca y cambia de estado).

### 5.4 Los patrones, y dónde se ven

| Patrón | Dónde |
|---|---|
| Repository + genérico base | `Shared/Persistence/BaseRepository.cs` |
| Servicio compuesto + reglas fuera | `Shared/Crud/` |
| **Decorador** | `CachedCategoryService` envuelve `CategoryService`; el servicio real no sabe que hay cache |
| **Puerto + adaptador** | `IFileStorage`, `IDocumentStore`, `ICatalogGateway`, `IReceiptRenderer`, `IEventPublisher` |
| **Null Object** | `NoCacheService`, `NoIdempotencyStore`, `NoAccessTokenDenylist`, `NoEntityRules` |
| **Outbox transaccional** | `IEventOutbox` + `OutboxPublisher` |
| **Inbox (exactamente una vez)** | `IMessageInbox` + tabla `ProcessedMessages` |
| **Plantilla por herencia de mecanismo** | `EventConsumer<TConsumer,TEvent>`: la subclase solo pone el efecto |
| Unidad de trabajo explícita | `ITransactionRunner` |
| Options + validación al arranque | las 11 clases de options |
| Filtros de acción | `[Idempotent]` (`Order -100`) |

⚠️ La elección de implementación (disco vs S3, Redis vs nada, broker vs nada) se hace **una
vez, al construir el grafo de DI**, no por petición. Eso es puerto+adaptador elegido en el
composition root, no Strategy.

### 5.5 DI y lifetimes

Cada feature registra lo suyo en un `XExtensions.cs` **dentro de su propia carpeta**.
`Shared/DependencyInjection/ServiceCollectionExtensions.cs` es el **composition root**:
**no registra servicios de dominio ni de infraestructura**, solo compone tres bloques que
declaran la dirección **Web → Features → Shared**. (Las dos únicas excepciones son
`AddControllers()` y `AddHsts()`: son superficie HTTP y no tienen otra carpeta a la que
pertenecer.)

```csharp
builder.Services
    .AddSharedInfrastructure(builder.Configuration)   // EF Core, Redis, disco, documentos, mensajeria
    .AddFeatures(builder.Configuration)               // un bloque por contexto acotado
    .AddWebApi(builder.Configuration);                // superficie HTTP
```

- **Scoped**: todo lo que dependa de `AppDbContext` — repositorios, `ICrudService`,
  `IEntityRules`, `ITransactionRunner`, `ICommandLog`, `IEventOutbox`, `IMessageInbox`.
- **Singleton**: lo que no guarda estado por request y solo depende de singletons —
  `IJwtTokenService`, `ICacheService`, `IIdempotencyStore`, `IAccessTokenDenylist`,
  `IFileStorage`, `IDocumentStore`, `IReceiptRenderer`, `RabbitMqConnection`,
  `IEventPublisher`.
- ⚠️ **Un servicio se registra con el MISMO lifetime en todas sus ramas.** Una rama Scoped y
  otra Singleton es una mina que solo estalla en el entorno que sí tiene la infraestructura.

## 6. La superficie HTTP

Rutas **versionadas por segmento**: `[Route("api/v{version:apiVersion}/[controller]")]` +
`[ApiVersion("1.0")]` → todo cuelga de `/api/v1/…`.

| Controller | Endpoints |
|---|---|
| `CategoryController` | CRUD + `/paged`. Lecturas anónimas, escrituras `admin` |
| `ProductController` | CRUD + `/paged` + `/category/{id}` + `/search` + `/{id}/image` + **`POST /buy`** |
| `AuthController` | `register`, `login`, `refresh`, `logout`, `logout-all`, `password`, `me` |
| `UserController` | listado, detalle, roles, bloqueo — todo `admin` |
| `OrderController` | `POST /order`, `GET /{id}`, `GET /paged`, **`GET /{id}/receipt`** (PDF) |
| `HealthController` | `GET /health` — `[ApiVersionNeutral]` |

- ⚠️ **Varios `[Authorize]` se COMBINAN (AND)**, no se sobreescriben. Solo
  `[AllowAnonymous]` gana sobre el de la clase. Por eso el requisito **débil** va en la
  clase y cada acción añade el suyo.
- ⚠️ Un controller de infraestructura **sin `[ApiVersion]` da 404**: necesita
  `[ApiVersionNeutral]`.
- ⚠️ `CreatedAtRoute` con ruta versionada **necesita el parámetro `version`** explícito
  (`HttpContext.ApiVersionValue()`), o el `Location` falla con un 500 sin relación.
- Swagger genera **un documento por versión descubierta**, así que añadir una v2 no toca
  `Program.cs`.

**`POST /product/buy` y `POST /order` conviven a propósito**: el primero es una operación de
*catálogo* que solo mueve el stock y no deja rastro; el segundo es el checkout de
*Ordering*, multi-línea, que congela precios, numera la orden y genera comprobante.

### Orden del pipeline (`Program.cs`) — no es decorativo

`UseForwardedHeaders` → `CorrelationIdMiddleware` → `UseSerilogRequestLogging` →
`UseExceptionHandler` → **`ClientAbortMiddleware`** → `UseStatusCodePages` → Swagger (dev) →
HSTS (fuera de dev) → `UseHttpsRedirection` → `UseCors` → `UseRateLimiter` →
`UseStaticFiles` → `UseAuthentication` → `UseAuthorization` → endpoints.

⚠️ `ClientAbortMiddleware` va **por debajo** de `UseExceptionHandler`: el middleware de
diagnóstico del framework escribe su «unhandled exception» a nivel Error **antes** de llamar
a ningún `IExceptionHandler`, así que decidirlo en el handler llega tarde.
⚠️ `UseCors` va **antes** de auth (un preflight `OPTIONS` no lleva token) y `UseStaticFiles`
después del rate limiter (es terminal para lo que sirve).

## 7. Las garantías de producción

Es lo que distingue este repo de un CRUD. Cada regla nace de un bug **medido**, no de teoría.

### 7.1 Concurrencia: una herramienta por problema

| Problema | Herramienta |
|---|---|
| Contador con contención (stock, saldo, cupos) | **UPDATE condicional atómico** (`ExecuteUpdateAsync` con `WHERE`) |
| Editar una entidad (dos admins a la vez) | **Concurrencia optimista** (`[Timestamp] RowVersion` + `ETag`/`If-Match` → 412) |
| Unicidad de un campo | **Índice único en la BASE** (la regla aplicativa solo da el mensaje bonito) |
| Doble submit / reintento del cliente | **Marca del comando en la misma transacción que el efecto** (§7.2) |
| Escribir en BD y en el broker | **Outbox transaccional** (§7.3) |

- ⚠️ `RowVersion` **NO sirve para el stock**: se midió, rechazaba compras válidas al agotar
  reintentos.
- ⚠️ `ExecuteUpdateAsync` **no pasa por `SaveChangesAsync`**: no dispara la auditoría (hay
  que poner `UpdatedAt` a mano) y no toca el change tracker (hay que releer). Y
  `DateTime.Now` **dentro** del árbol de expresión se traduce a `GETDATE()` — hay que
  capturarlo en una variable local.
- ⚠️ `nvarchar(max)` **no es indexable** en SQL Server: un campo con índice único necesita
  `[MaxLength]` **en la entidad**.
- ⚠️ **Adquirir locks siempre en el mismo orden global.** Recorrer las líneas de un carrito
  en el orden que mandó el cliente es pedir un deadlock; se ordenan por SKU.

### 7.2 Idempotencia: la garantía y el atajo

- **GARANTÍA**: `ICommandLog` escribe la marca del comando en la tabla `ExecutedCommands`
  **dentro de la misma transacción que el efecto**. El árbitro entre réplicas es la **clave
  primaria**: no hacen falta ni reserva, ni TTL, ni estado «en curso».
- **ATAJO**: `[Idempotent]` reserva en Redis (`SET NX`) para frenar duplicados **en vuelo**
  antes de que se apilen sobre la misma fila. Si Redis no está, esto se salta y no pasa nada.
- La operación recibe una **`CommandIntent` obligatoria** en la firma, y devuelve
  `CommandOutcome<T>` (resultado + `WasReplayed`), que el controller traduce a la cabecera
  `Idempotency-Replayed`.

⚠️ **Por qué**: con la marca en Redis, fuera de la transacción, quedaban dos ventanas que
ningún código cierra. Medido: **174 de 14.400 peticiones se ejecutaron sin garantía con
Redis sano**. Una garantía no puede vivir fuera de la transacción que protege.
⚠️ La retención de `ExecutedCommands` (`Outbox:RetentionDays`) tiene que **cubrir el peor
reintento de un cliente**: pasado ese plazo, la misma clave vuelve a ejecutar de verdad.

### 7.3 Mensajería: el camino completo de un evento

```
servicio (dentro de su transaccion)
   -> IEventOutbox.EnqueueAsync          fila en OutboxMessages, MISMA transaccion
OutboxPublisher (BackgroundService)
   -> sp_getapplock -> lote por Sequence -> publica con publisher confirms -> marca ProcessedAt
RabbitMQ  exchange topic, routingKey = EventType
   -> cola del slice (una EventSubscription por consumidor)
EventConsumer<TConsumer,TEvent>
   -> IMessageInbox.ProcessOnceAsync(messageId, tipo, EFECTO)   marca + efecto, misma transaccion
   -> fallo: cola de espera con TTL -> vuelve a la principal (N intentos)
   -> agotados: OnExhaustedAsync -> nack sin requeue -> DLX propia -> {cola}.dlq
```

- **Publicar primero, marcar después**: at-least-once. Se prefiere duplicar a perder, y el
  inbox deduplica por `MessageId`.
- **El evento y su consumidor viven en el SLICE que los emite**; `Shared/Messaging` solo
  pone el mecanismo. Un consumidor nuevo declara su `EventSubscription` y la pasa a
  `AddEventConsumer<T>(config, subscription)`.
- **El efecto vive fuera del `BackgroundService`** (`IProductPurchasedHandler`,
  `IReceiptGenerator`) para que se pueda probar sin broker. Es la lección de `planning/18`.

⚠️ **Un evento sin cola que lo acepte no falla, se pierde de vista**: el publicador usa
`mandatory: true` con confirms, así que vuelve como **312 NO_ROUTE**, el outbox lo cuenta
como intento y se agota. La operación funciona y el consumidor no se entera nunca.
⚠️ **Redeclarar una cola existente con otros argumentos da 406 PRECONDITION_FAILED** y deja
la mensajería abajo. Es un error de **configuración**, permanente, y reintentar no lo
arregla. Por eso el TTL de la cola de espera va **en su nombre** (cambiarlo es aditivo) y la
dead-letter es un **campo** de la suscripción, no una fórmula.
⚠️ **Una DLX por cola.** Es `fanout`: dos colas apuntando a la misma repartirían los
mensajes muertos de un slice a la dead-letter del otro.
⚠️ **`Sequence` no garantiza el orden** de publicación: el `IDENTITY` se asigna al INSERT y
la fila se ve al COMMIT. Es determinista y repetible, no ordenado.
⚠️ Un `BackgroundService` que lanza **muere y no vuelve**, y desde .NET 6 el default es
`StopHost`: **tumba la API entera**. El patrón es
`catch (OCE) when (stoppingToken.IsCancellationRequested) { break; }` y luego
`catch (Exception)` **sin filtro**.

### 7.4 Toda degradación es explícita, y solo degrada lo que es una optimización

- **Una optimización tiene fuente de verdad alternativa** y degrada en abierto, nunca con un
  500: la cache cae → se lee de la base; el broker cae → el evento espera en el outbox.
- **Una garantía no tiene plan B**, así que no puede vivir fuera de la transacción que
  protege (§7.2).
- **La decisión debe ser la MISMA en todas las implementaciones de una interfaz.**
- **Renunciar a una garantía lo decide el cliente**, no nosotros en silencio: por HTTP eso
  es *no mandar* la cabecera `Idempotency-Key`.

### 7.5 Documentos privados ≠ archivos públicos

`IFileStorage` guarda imágenes de producto **dentro de `wwwroot/`** para que
`UseStaticFiles` las sirva a cualquiera: son públicas y esa es su gracia. `IDocumentStore`
guarda comprobantes **fuera de `wwwroot/`**, con una **clave opaca** que es lo único que se
persiste en base de datos, y se sirven por un endpoint que comprueba de quién es la orden.

⚠️ **No se fusionan.** Dos necesidades opuestas no caben detrás de la misma abstracción por
mucho que las dos «guarden ficheros». La pregunta que las separa no es «¿qué hace?» sino
**«¿quién puede leerlo?»**.
⚠️ Lo que se persiste es **la clave, nunca la ruta**: guardar `/app/App_Data/…/x.pdf` ataría
la base a la infraestructura de hoy, y migrar a S3 obligaría a reescribir todas las filas.

## 8. Errores

`Exceptions/` define la jerarquía `AppException`, cada una con su `Code` estable y su
`HttpStatusCode`: `BadOperationAppException` 400, `UnauthorizedAppException` 401,
`ForbiddenAppException` 403, `NotFoundAppException` 404, `ConflictAppException` 409,
`PreconditionFailedAppException` 412, `ValidationAppException` 422,
`IdempotencyConflictAppException` 422, y `CustomAppException(code, msg, status)` para un
código propio.

`Shared/Http/GlobalExceptionHandler.cs` (un `IExceptionHandler`) las traduce a
**RFC 7807 `ProblemDetails`**, con `code` y `correlationId` en las extensiones.
**Los controllers no llevan `try/catch` de negocio**: las reglas lanzan, el handler mapea.

- **400 vs 409**: el request es inválido en sí mismo, o es válido pero choca con el estado.
  **401 vs 403**: «no sé quién eres» vs «sé quién eres y no puedes».
- Un listado sin resultados es **200 con `[]`**, nunca 404. Una página fuera de rango
  también.
- El handler traduce además las **carreras que arbitra la base** (choque de índice único →
  409, deadlock → 409, FK → 409, timeout → **503 con `Retry-After`**). No es código viejo:
  sin eso, arreglar una carrera empeoraría la respuesta convirtiendo un 409 correcto en 500.
- ⚠️ **No hagas pattern matching sobre la FORMA del anidamiento**: `SaveChangesAsync`
  envuelve el `SqlException` en `DbUpdateException`, `ExecuteUpdate*` lo lanza **desnudo**.
  Se recorre la cadena de `InnerException`, y se filtra por **número** de error, no por tipo.
- ⚠️ La red de seguridad para excepciones BCL es **estrecha a propósito**:
  `InvalidOperationException` **no** se mapea (EF la usa para errores de programación, y
  mapearla filtraba mensajes internos del ORM al cliente).
- ⚠️ Un cliente que cuelga **no es un fallo del servidor**: sale como **499** a nivel
  Information desde `ClientAbortMiddleware`.

## 9. Convenciones

- Namespaces file-scoped; dos espacios de indentación casi en todas partes (los controllers
  usan cuatro); `Nullable` e `ImplicitUsings` activados.
- **Constructores primarios** en repositorios, infraestructura y la mayoría de los
  servicios; los **controllers** usan constructor clásico, sin excepción.
- Lecturas con `.AsNoTracking()`; los listados ordenan por `CreatedAt` descendente **con
  desempate por clave primaria**. ⚠️ El desempate no es cosmético: `CreatedAt` no es único
  —lo estampa `DateTime.Now`— y sin un orden total, cada página es un `OFFSET/FETCH`
  independiente y una fila puede salir en dos páginas y otra en ninguna. Lo aplica
  `BaseRepository.ApplyDefaultOrder`; **un repositorio que escriba su propio `OrderBy`
  tiene que repetirlo**.
- `CancellationToken ct = default` es **el último parámetro de toda operación async**,
  hilado controller → servicio → repositorio → EF.
- **Las actualizaciones son `PATCH`**, no `PUT`, y mapean el DTO sobre la entidad rastreada.
  **Todo campo de un `UpdateXDto` es nullable, tipos de valor incluidos.**
- La validación vive en los DTOs con DataAnnotations; el controller comprueba
  `ModelState.IsValid` y devuelve `ValidationProblem(ModelState)`.
- `[ProducesResponseType]` en cada acción, y rutas con nombre para que `CreatedAtRoute`
  pueda referenciarlas.
- **Timestamps con `DateTime.Now`** (local, no UTC) en todo el modelo, estampados por
  `AppDbContext.SaveChangesAsync` — **nunca a mano**. La excepción son los `exp`/`nbf` del
  JWT, que la RFC 7519 exige en UTC.
- **Mapping (AutoMapper)**: un profile por entidad. El PATCH parcial se expresa campo a
  campo con `MapFrom((s, d) => s.X ?? d.X)`, **no** con
  `ForAllMembers(o => o.Condition(...))` — `Condition` recibe el valor ya convertido al tipo
  destino, así que un `int?` nulo llega como `0`, no se salta, y pisa el campo. Con
  `CategoryId` eso rompe la FK y devuelve un 500. `CreatedAt`/`UpdatedAt` y las navegaciones
  se `Ignore()` siempre al escribir.
- ⚠️ **Los bloques comentados de implementaciones anteriores NO se borran**: son el registro
  de aprendizaje del autor. Y **cuidado al editar con scripts**: una sustitución global se
  cuela dentro de ellos y los rompe (ya pasó dos veces).

## 10. Trampas ya pisadas — no volver a caer

| Trampa | Qué pasa |
|---|---|
| Varios `[Authorize]` | Se **combinan (AND)**. Solo `[AllowAnonymous]` gana. |
| `CreatedAtRoute` versionado | Sin el parámetro `version`, 500 sin relación aparente. |
| Controller sin `[ApiVersion]` | 404. Los de infraestructura llevan `[ApiVersionNeutral]`. |
| Serilog | Lee la sección **`Serilog`**, no `Logging`. Y la plantilla de fábrica **no renderiza el `LogContext`**: sin `outputTemplate`, el `CorrelationId` no sale en ninguna línea. Declarar el sink en código **y** en config **duplica** cada línea. |
| Comentarios `//` en `Serilog:MinimumLevel:Override` | Serilog resuelve **cada clave** como nombre de logger: el arranque muere con `No LoggingLevelSwitch has been declared with name …`. |
| `[Required]` en options | Se valida al leer `.Value` aunque la feature esté apagada. Regla condicional ⇒ `.Validate(...)`. |
| Arrays de configuración | Se **fusionan** por índice, no se reemplazan. |
| `nvarchar(max)` | No es indexable: índice único ⇒ `[MaxLength]` en la entidad. |
| `ExecuteUpdateAsync` | No dispara auditoría, no toca el change tracker, y `DateTime.Now` dentro se traduce a `GETDATE()`. |
| `AddPersistence` sin `EnableRetryOnFailure` | `CreateExecutionStrategy()` deja de reintentar y la unidad transaccional se vuelve un no-op. Es **load-bearing**. La contrapartida: EF prohíbe `BeginTransactionAsync` fuera de `strategy.ExecuteAsync`. |
| `[Transactional]` | **No se usa.** `ActionExecutionDelegate` no es reentrante y con reintentos ejecutaría la acción dos veces. La transacción va en el servicio con `ITransactionRunner`. |
| Marca de idempotencia y efecto | Van en la **misma transacción**. Confirmar la marca antes hacía que un efecto fallido se reconociera como duplicado y el mensaje **desapareciera sin procesarse**. |
| Publicar sin *publisher confirms* | `BasicPublishAsync` vuelve sin excepción aunque el mensaje no llegue a ninguna cola. **Nunca hacer ack después de un publish sin confirms.** |
| Fuga de conexiones AMQP | Si la declaración de topología falla hay que hacer `DisposeAsync` de la conexión local: con `AutomaticRecoveryEnabled` queda viva para siempre. Medido: 22 fallos = 22 conexiones fugadas. |
| **406 PRECONDITION_FAILED ≠ broker caído** | Es una cola que ya existe con otros argumentos. Es configuración, es permanente, y tratarlo como caída deja la mensajería abajo en bucle. |
| Redis caído no puede ser `Unhealthy` | Como la caída la ven todas las réplicas, el orquestador las sacaba **todas** de rotación por una dependencia opcional. Va en `Degraded`. |
| `dotnet ef … --no-build` | Usa el ensamblado viejo → `PendingModelChangesWarning` sin sentido. |
| `dotnet ef` global vs EF Core | El global es **10.x** y el proyecto usa **EF Core 9**. Funciona, pero es lo primero que hay que mirar si una migración se comporta raro. |
| Dos `dotnet test` a la vez | Comparten la base `ApiEcommerceNET8_Tests`, que `ApiFactory` **borra al empezar**: la otra corrida ve 500 por todas partes. Parece un bug del código y no lo es. |
| Rate limiter propio | Al hacer pruebas de carga **te limita a ti**: reinicia el proceso para resetear la ventana. |
| Lockout de Identity | 5 logins fallidos bloquean la cuenta 5 min. Probar con un usuario nuevo. |
| **Tests: `UseSetting`, no `ConfigureAppConfiguration`** | Varias piezas leen la config **eager** para decidir qué implementación registran, y eso pasa **antes** de esos callbacks: el host arrancaba con las implementaciones nulas y los tests pasaban sin probar nada. |
| Tests de integración en paralelo | Comparten base de datos: van todos en la misma colección, **incluidos los que levantan su propio host**. |
| FluentAssertions | **No se usa** aunque la skill la pida: desde la v8 exige licencia comercial. `Assert` de xunit basta. |
| `POST /api/v1/category` | Devuelve **201 sin cuerpo**: el id sale de la cabecera `Location`. No es un fallo. |

## 11. Cómo se trabaja aquí

### Antes de escribir código (`rules.md` §10)

Para toda tarea no trivial: **spec → planning → código**.

1. `AGENTS/features/NN_<slug>.feature` — Gherkin con keywords en **inglés** y descripciones
   en **español**. No es decorativo: cada `Scenario` debería poder convertirse en un test.
2. `AGENTS/planning/NN_<slug>.md` — checklist técnico ejecutable.
3. Solo entonces, implementar.

### Verificación — el suelo innegociable (`rules.md` §11)

- **`dotnet build` limpio, 0 warnings** (la CI compila con `-warnaserror`).
- **`dotnet test tests/ApiEcommerce.Tests` en verde.**
- **Lo que toque concurrencia, dependencias externas o el arranque se prueba EJECUTANDO**,
  no compilando: concurrencia con peticiones **simultáneas** (`for … & done; wait`),
  degradación con la dependencia **caída**, arranque con `ASPNETCORE_ENVIRONMENT=Production`
  y la configuración mínima.
- **Revisar siempre la migración generada** antes de aplicarla: EF a veces propone un
  drop/recreate que pierde datos.
- **Para trabajo grande, revisión multiagente** (`rules.md` §9), un agente por eje
  (concurrencia / seguridad / infraestructura) con la instrucción de **verificar
  ejecutando**, no de opinar. Es como se han encontrado los bugs que el build y los tests no
  veían.

### Versionado (`rules.md` §12)

- El agente **sí** commitea, con mensaje en español que explique el **porqué** y liste lo
  verificado. El agente **nunca** hace `git push` ni toca ramas remotas.
- Se trabaja en `dev`, **nunca** directamente en `main`.
- **`AGENTS/docs/06-estado-y-roadmap.md` y `AGENTS/progress.md` se actualizan en el MISMO
  commit** que el cambio que describen. Un roadmap desactualizado hace que quien lo lea
  —persona o agente— trabaje sobre una foto falsa del repo.
- `notes.md` recibe un capítulo por feature, en el estilo exacto del archivo. Petición
  literal del owner: **«NOOO me llenes de texto innecesario ni de contenido no útil»** — se
  escribe solo lo **no obvio**: el gotcha, el porqué, el comando exacto y, al portar algo
  del curso, qué hacía mal el original.

### Skill instalada

`dotnet-best-practices` (`.agents/skills/`, enlazada en `.claude/skills/`). **Precedencia:
`AGENTS/docs/` gana.** Sus secciones sobre Semantic Kernel, `ResourceManager` para
localización, patrón Command Handler y MSTest+FluentAssertions **no aplican**. Y su consejo
de «evitar duplicación mediante clases base» aquí solo vale para `BaseRepository<T>`: en la
capa de servicio la reutilización es por **composición**.

### Material de referencia

`AGENTS/__ref__/01/code` es el repo del curso del que nació el proyecto (ramas
`fin-seccion-*`). Es **material de origen, no autoridad**: se trae la *feature*, nunca el
*código*, y se anota en `notes.md` qué hacía mal el original.
