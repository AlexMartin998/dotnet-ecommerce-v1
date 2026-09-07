# 06 — Estado actual y roadmap

Foto del repo al **2026-08-30**, después de traer a esta arquitectura las
features de las secciones 8–15 del curso de referencia
(`AGENTS/__ref__/01/code`): autenticación, CORS, cache, versionado, imágenes,
paginación y seeding.

## Estado por componente

| Componente | Estado | Nota |
| --- | --- | --- |
| `AppDbContext` + migraciones | ✅ | 14 migraciones aplicadas (la última, `UniqueEmailAndSessionRevocationIndex`); auditoría automática en `SaveChangesAsync` |
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
| Registro de DI | ✅ | un `XxxExtensions.cs` por feature, en su carpeta; `Shared/DependencyInjection/` es solo composition root (`AddSharedInfrastructure`/`AddFeatures`/`AddWebApi`) |
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
| Concurrencia: unicidad | ✅ | índice único en `Category.Name`, `Product.SKU`, `Order.Number` y `AspNetUsers.NormalizedEmail`; `DbUpdateException` → 409 |
| Concurrencia: último administrador | ✅ | `sp_getapplock` exclusivo en la transacción de la baja: recontar y quitar el rol dejan de ser dos viajes |
| Concurrencia optimista | ✅ | `Product.RowVersion` para el PATCH; `DbUpdateConcurrencyException` → 409 |
| Idempotencia de peticiones | ✅ | `[Idempotent]` + `Idempotency-Key` sobre Redis (`SET NX`) |
| Outbox transaccional | ✅ | `OutboxMessage` en la misma transacción que el negocio; la API funciona con el broker caído |
| Publicación a RabbitMQ | ✅ | `OutboxPublisher` (BackgroundService) con publisher confirms y mensajes persistentes |
| Reintentos del outbox | ✅ | **un broker caído no consume intentos**: solo los cuenta el fallo atribuible a un mensaje (`BrokerUnavailableException`) |
| Consumidor | ✅ | `EventConsumer<TConsumer,TEvent>` (fontanería AMQP compartida) + `ProductPurchasedConsumer` y `OrderPlacedConsumer`: ack manual, prefetch, DLQ, dedupe por `ProcessedMessage` |
| Varias colas / varios consumidores | ✅ | cada slice declara su `EventSubscription`; `RabbitMqConnection` declara la topología de todas, con **una DLX por cola** |
| **Órdenes** (`Features/Ordering`) | ✅ | slice completo: `Order`/`OrderItem`, número por secuencia, puerto `ICatalogGateway`, `POST /api/v1/order` transaccional e idempotente |
| Comprobante en PDF | ✅ | **asíncrono** por `OrderPlaced` → `IReceiptGenerator`; QuestPDF detrás de `IReceiptRenderer` |
| Almacén de documentos privados | ✅ | `IDocumentStore` + `LocalDocumentStore`, **fuera de `wwwroot`**, clave opaca; cambiar a S3/R2/MinIO/Cloudinary es una implementación y una línea |
| Dockerfile + compose | ✅ | multi-stage, usuario `$APP_UID` de la imagen base, `curl` instalado para el healthcheck |
| Migraciones al arrancar | ✅ | `MigrateAsync()` en el scope de arranque (la imagen runtime no lleva `dotnet-ef`) |
| Idempotencia de la config | ✅ | validación condicional de `SeedOptions`; `ValidateOnStart` en todas las secciones |
| `UseForwardedHeaders` | ✅ | el rate limiter particiona por la IP real, no por la del proxy |
| Sonda de backlog del outbox | ✅ | `outbox-backlog` → `Degraded` si hay eventos que agotaron reintentos; el umbral sale de `RabbitMq:MaxPublishAttempts`, el mismo que aplica el publicador |
| Tests | ✅ | `tests/ApiEcommerce.Tests`: **274** (unitarios + integración + concurrencia + degradación y arranque) |

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
idempotencia, las **cinco carreras** con `Task.WhenAll` (que secuencialmente pasaban
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

### Paso 8.bis — Idempotencia bajo carga — ✅ **cerrado** (2026-09-06)

Fase de revisión, sin feature nueva: validar `Idempotency-Key` contra
infraestructura real y con carga de verdad. Detalle en
[`planning/16`](../planning/16_idempotencia-bajo-carga.md).

Lo que **aguantó**: exactamente-una-vez con ráfagas de hasta 350 simultáneas y
—por primera vez comprobado— **entre dos réplicas** contra el mismo Redis, que es
la razón por la que el store no vive en memoria.

Lo que se **corrigió**: la reserva ahora se toma con `SET NX GET` (un viaje en vez
de dos, 3,00 → 2,00 operaciones por petición) y lleva **token de propiedad**, así
que un `Release` tardío no puede borrar la reserva de otro; la huella del cuerpo se
compara en **todos** los caminos, no sólo en el secuencial; los plazos pasan a
`IdempotencyOptions`; y la clave del cliente tiene límite.

Lo que quedó abierto —el almacén degradaba en abierto bajo carga y ejecutaba sin
garantía, 174 de 14 400— lo cierra el paso siguiente.

### Paso 8.ter — La idempotencia, de optimización a garantía — ✅ **cerrado** (2026-09-06)

Detalle en [`planning/17`](../planning/17_idempotencia-transaccional.md).

La pregunta que dejó el paso anterior («¿fallamos en cerrado con 503?») **estaba mal
planteada**. La marca de que un comando ya se ejecutó pasa a `ExecutedCommands`,
escrita en la **misma transacción que el efecto** y arbitrada por su clave primaria —
el *inbox pattern* que documenta Azure, lo que hace Stripe, y lo que este repo ya
hacía bien en `ProductPurchasedConsumer`. Con eso, «el almacén no está» y «la
operación no puede ocurrir» son el mismo evento.

`BuyAsync` recibe una `CommandIntent` **obligatoria**: la garantía deja de depender
de que el controller lleve un atributo, igual que se hizo con la transacción.
`[Idempotent]` se queda en **puerta de admisión** para duplicados en vuelo.

Verificado con Redis apuntando a un puerto muerto: 40 simultáneas con la misma clave
descuentan **1** y todas reciben 200. Antes era imposible por diseño. Y la identidad
byte a byte del replay se arregló sola al memorizar el DTO en vez de la respuesta HTTP.

### Paso 8.quater — Deuda de mensajería — ✅ **cerrada** (2026-09-06)

Detalle en [`planning/18`](../planning/18_deuda-de-mensajeria.md). Cierra
`planning/12` §12.5 **entera**.

El hallazgo transversal: **lo que no se podía probar era lo que vivía dentro de un
`BackgroundService`**. El efecto sale a `IProductPurchasedHandler`, la unidad
transaccional a `IMessageInbox` —gemelo de `IEventOutbox`— y el contador de intentos
a `RetryAttempts`. Con eso, el test del P0 del consumidor —pendiente desde que se
arregló el bug— se escribe sin broker.

También: contador de reintentos en cabecera propia (un replay desde la DLQ ya vuelve
con presupuesto), `RetryDelaySeconds` configurable de verdad (el TTL va en el nombre
de la cola, así que cambiarlo no da 406), canal AMQP reutilizado, y `global.json` con
la CI instalando los dos SDK.

⚠️ Y un defecto que abrió ese mismo cambio y **solo se vio ejecutando**: con las colas
de espera ligadas a un exchange, cada reintento se copiaba a todas. Se publica al
exchange por defecto con el nombre de la cola como routing key.

### Paso 8.quinquies — Los «errores» que no eran errores — ✅ **cerrado** (2026-09-06)

Detalle en [`planning/19`](../planning/19_errores-bajo-carga.md).

Un cliente que cuelga hacía que EF cancelara el `SqlCommand` y SqlClient lanzara un
**`SqlException`** —no una `OperationCanceledException`— así que salía como 500 con
traza completa: 27 «errores» que no lo eran en una sola prueba de carga.
`ClientAbortMiddleware` lo absorbe (**499**, Information, sin traza), y un timeout
de base (`SqlException` −2) pasa a **503 + `Retry-After`** porque es reintentable.

⚠️ El middleware va **por debajo** de `UseExceptionHandler`: el diagnóstico del
framework escribe su línea de Error **antes** de llamar a ningún `IExceptionHandler`,
así que decidirlo en `GlobalExceptionHandler` llega tarde.

### Paso 9 — Partir en proyectos (cuando duela, no antes)

Los tres bloques del composition root (`AddSharedInfrastructure` / `AddFeatures` /
`AddWebApi`) son ya las costuras, y con el vertical slicing la línea de corte más natural
ya no es por capa técnica sino **por contexto acotado**: `ApiEcommerce.Api` +
un proyecto por slice + `.Shared`. Hoy la dirección de dependencias es una convención;
partir en proyectos la convierte en algo que impone el compilador. **No es
urgente**: hacerlo antes de que el proyecto lo pida solo añade fricción.

### Paso 10 — Refresh tokens y revocación — ✅ **hecho** (2026-09-06)

Detalle en [`planning/13`](../planning/13_refresh-tokens.md).

Hasta ahora un access token robado valía 60 minutos y no había forma de invalidarlo.
Ahora el access token dura **15 minutos** y se renueva con un refresh token que viaja
en **cookie `HttpOnly`** (decisión del owner), con **rotación y detección de reuso**:
reusar un token ya gastado fuera de la ventana de gracia revoca la familia entera.

⚠️ La ventana de gracia no es un parche: sin ella, dos refrescos en paralelo de un
cliente legítimo —lo normal en un móvil o una SPA— parecen un robo y cierran la
sesión sin parar.

Misma división que la idempotencia: la **garantía** («esta sesión no se puede
extender») es la familia revocada en la base, en una transacción; la **optimización**
es la denylist de `jti` en Redis, que solo adelanta la muerte del access token que el
cliente ya tiene. Verificado con Redis muerto: el logout sigue cortando la sesión.

### Paso 11 — Administración de usuarios — ✅ **hecho** (2026-09-06)

Detalle en [`planning/14`](../planning/14_admin-usuarios.md). Listado paginado,
roles y bloqueo, con `UserManager` y no con el CRUD genérico.

🔴 Lo que destapó: **bloquear una cuenta no servía de nada**. Identity comprueba el
bloqueo en el login, pero el usuario seguía dentro con su access token y podía
**seguir renovándolo indefinidamente**, porque renovar no vuelve a pedir
credenciales. Cerrado por los dos lados: bloquear revoca las sesiones, y renovar
comprueba el bloqueo.

### Paso 11.bis — Órdenes y comprobante en PDF — ✅ **hecho** (2026-09-06)

Detalle en [`planning/20`](../planning/20_ordenes-y-comprobante.md). Cuarto contexto
acotado (`Ordering`), el primero que se añade con el slicing ya asentado: crear la
carpeta y **una línea** en `AddFeatures()`.

Las tres decisiones que lo ordenan: el PDF **no se genera en la petición** (evento por
el outbox y un consumidor aparte), la base guarda una **clave opaca** y no una ruta, y
lo que se copia en la orden **se congela**.

Lo que hubo que resolver por debajo: `Shared/Messaging` servía a **una** cola. Publicar
un segundo evento sin tocarlo fallaba en silencio —`312 NO_ROUTE`, el outbox agotando
intentos, la compra bien y el comprobante nunca—. Ahora cada slice declara su
`EventSubscription` y la fontanería AMQP se hereda de `EventConsumer<,>`.

🔴 Y una medición destapó algo **anterior a esta tarea**: cada 4xx de dominio escribía un
«unhandled exception» a nivel Error con traza. 30 compras simultáneas sobre stock 20
dejaban 10 incidentes falsos; un 404 de categoría, lo mismo. Silenciada la línea
duplicada del framework — `GlobalExceptionHandler` ya registraba todo, y mejor.

🔴 La **revisión multiagente** (`rules.md` §9) encontró dos defectos serios que ni el build
ni los tests veían: `ReceiptStatus.Failed` era **inalcanzable** (un comprobante muerto en la
DLQ dejaba al cliente con un 409 «vuelve luego» para siempre) y el descuento de stock
recorría las líneas en el orden del carrito, o sea un **deadlock** entre carritos con los
mismos SKU en distinto orden. Además: la canonicalización de rutas no seguía enlaces
simbólicos, una barra final en `Documents:RootPath` rompía el almacén en silencio, y
`?page=2147483647` daba un 500 en **todos** los `/paged` (previo). Todo corregido y con
test.

### Paso 11.ter — Recuperar de la DLQ y recoger basura — ✅ **hecho** (2026-09-06)

Detalle en [`planning/21`](../planning/21_recuperar-comprobantes-y-recoger-basura.md).
Cierra las dos deudas de `planning/20`: un comprobante que agotaba sus reintentos dejaba la
orden en `failed` —correcto— pero **reemitirlo exigía entrar al broker a mano**, y el
almacén no tenía quien recogiera lo que nadie apunta.

`GET /api/v1/dead-letter` + `POST /{queue}/replay` (solo admin; la cola llega en la petición
y se valida contra las suscripciones registradas, que son una **allowlist por
construcción**), y un recolector de huérfanos con **periodo de gracia** — la única línea de
todo esto que no se puede equivocar, porque el PDF se escribe dentro de la transacción y
existe antes que la fila que lo referencia.

Verificado ejecutando el ciclo completo contra RabbitMQ real: morir → verlo en la DLQ →
arreglar → reemitir → la orden vuelve a `available` con su PDF.

### Paso 12 — Deudas conocidas y anotadas

- **`DateTime.Now` → `DateTimeOffset`/UTC.** Ya se nota la inconsistencia: los
  `exp`/`nbf` del JWT van en UTC (lo exige la RFC 7519) mientras el resto del
  modelo usa hora local, y `UpdatedAt` recién estampado se serializa con offset
  mientras el leído de base sale sin él. Es una migración de todo a la vez:
  entidades, DTOs y datos.
- **El orden por defecto ya tiene desempate estable** (`BaseRepository.ApplyDefaultOrder`,
  `CreatedAt` desc + clave primaria). ⚠️ Un repositorio que escriba su propio `OrderBy`
  tiene que repetirlo: sin orden total, paginar repite filas y se salta otras.
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
- **Solo `POST /product/buy` declara intención de comando.** El mecanismo es
  general; aplicarlo a otra operación es añadir el parámetro y dos líneas.
- **La retención de `ExecutedCommands` va con `Outbox:RetentionDays`**, que ahora
  gobierna tres tablas. ⚠️ Ese plazo tiene que cubrir el peor reintento de un
  cliente: a partir de ahí, la misma clave vuelve a ejecutar de verdad.
- ✅ **Documentos huérfanos y salida de la DLQ: cerrado** en `planning/21`. Hay recolector
  (con periodo de gracia, porque el fichero existe antes que la fila que lo apunta) y
  endpoint de administración para reemitir. Un PDF truncado **no** necesitaba caso especial:
  como `SaveAsync` nunca devolvió clave, cae por la misma regla.
- **Nadie ALERTA cuando la DLQ crece.** Ya se puede consultar y vaciar, pero hay que mirar.
  La sonda `outbox-backlog` hace algo parecido para el outbox y sería su gemela; se deja
  fuera porque alertar sin destinatario no es alertar.
- **La transacción del inbox sigue abierta durante el render del PDF.** No sostiene locks
  (el `SELECT` los suelta al terminar la sentencia y el `UPDATE` viene después), así que hoy
  el coste es una conexión del pool ocupada. Vale la pena mirarlo si se sube el prefetch o
  el documento se vuelve pesado; moverlo fuera exigiría que `ProcessOnceAsync` aceptara una
  fase previa no transaccional, y eso debilita la garantía.
- **Un solo vendedor por orden.** La imagen de referencia era un marketplace con
  sub-órdenes por *seller*. Cuando haya sellers, la orden se parte y el comprobante también.
- **Cancelar o devolver una orden.** Devolver stock tiene sus propias invariantes; no se
  improvisa junto a esto.
- **Rotación de `Jwt:SecretKey` en caliente.** `JwtTokenService` es singleton y
  materializa las `SigningCredentials` en el constructor, así que rotar la clave
  exige reiniciar. Se resuelve cambiando `IOptions` por `IOptionsMonitor`.

## Cómo mantener este documento

Al terminar un paso, actualizar la tabla de estado en el mismo commit que el
código. Un roadmap desactualizado es peor que no tenerlo: hace que quien lo lee
(persona o agente) trabaje sobre una foto falsa del repo.
