# AGENTS/memory.md — Norte de arranque de sesión

> **Léeme primero, siempre.** Esto es lo que un agente necesita saber en los primeros 30
> segundos de una sesión: qué es este repo, cómo se levanta, dónde está cada cosa y qué
> trampas ya hemos pisado. El detalle vive en `rules.md` (reglas duras),
> `docs/` (arquitectura, MANDA) y `progress.md` (avance y pendientes).

---

## 0. 🔖 Punto de continuación — **última sesión: 2026-09-06**

**Todo lo commiteado está en `dev`, árbol limpio, SIN push.** El agente commitea pero
nunca hace push (`rules.md` §12).

### Qué se hizo en la última sesión

**`planning/20` — órdenes y su comprobante en PDF.** Cuarto contexto acotado
(`Features/Ordering/`), el primero añadido con el slicing ya asentado: carpeta + una línea
en `AddFeatures()`, y toda la dependencia hacia `Catalog` en **una** clase (`CatalogGateway`).

1. **El PDF NO se genera en la petición**: la compra emite `OrderPlaced` por el outbox que
   ya existía y `OrderPlacedConsumer` lo genera aparte. La orden nace con
   `receiptStatus: "pending"`.
2. **`IDocumentStore`** (`Shared/Documents/`) — almacén de documentos **privados**, FUERA de
   `wwwroot`, con **clave opaca** en la base. Cambiar a S3/R2/MinIO/Cloudinary = otra
   implementación y una línea en `AddDocumentStorage`. ⚠️ **No se fusiona con
   `IFileStorage`**: aquel sirve imágenes públicas desde `wwwroot`, y ahí un comprobante lo
   descargaría cualquiera que adivinara la ruta.
3. **QuestPDF** detrás de `IReceiptRenderer` (validado: Community es gratis por debajo de
   1 M USD anuales — es un **umbral**, decisión del owner el día que aplique).
4. 🔴 **Había que generalizar la mensajería**: servía a UNA cola. `order.placed` volvía como
   **312 NO_ROUTE** y se agotaba en el outbox — compra bien, comprobante nunca. Ahora cada
   slice declara su `EventSubscription` y la fontanería AMQP se hereda de
   `EventConsumer<TConsumer,TEvent>`.
5. 🔴 **Y midiendo apareció un defecto ANTERIOR**: cada 4xx de dominio escribía un
   «unhandled exception» a nivel Error con traza. 30 compras simultáneas sobre stock 20 →
   **10 incidentes falsos**; un 404 de categoría, igual. Silenciada la línea duplicada del
   framework (`GlobalExceptionHandler` ya lo registraba, y mejor): de 10 a **0**.
6. **`planning/21` — la DLQ deja de ser un callejón sin salida**: `GET /api/v1/dead-letter`
   y `POST /{queue}/replay` (solo admin, la cola validada contra las suscripciones
   registradas: allowlist por construcción), más un **recolector de comprobantes huérfanos**
   con periodo de gracia. ⚠️ Ese periodo es la única línea que no se puede equivocar: el PDF
   se escribe dentro de la transacción, así que existe antes que la fila que lo apunta.
7. **Documentación regenerada contra el código** (§6.ter): `CLAUDE.md` tenía **~20
   afirmaciones falsas** —carpetas inexistentes, nombres del composition root inventados,
   «no hay proyecto de tests»— y ese archivo se carga en **toda** sesión. Reescrito con
   cuatro agentes de inventario y uno intentando refutar el resultado. 🔴 Y verificarlo
   destapó un bug: el orden por defecto de `BaseRepository` **no tenía desempate estable**,
   así que `/paged` podía repetir filas y saltarse otras.
8. **Revisión multiagente** (`rules.md` §9): 🔴 `ReceiptStatus.Failed` era **inalcanzable**
   (comprobante muerto en la DLQ = 409 `receipt_not_ready` eterno) → hook `OnExhaustedAsync`
   en `EventConsumer`; 🔴 **deadlock evitable** descontando stock en el orden del carrito →
   se ordenan las líneas por SKU; la canonicalización de rutas **no seguía enlaces
   simbólicos**; una barra final en `Documents:RootPath` rompía el almacén en silencio; y
   `?page=MAXINT` daba **500** en todos los `/paged` (previo).
9. **274 tests** (eran 202), build sin warnings. Verificado ejecutando: ciclo completo
   compra → evento → PDF, **30 simultáneas → 20 órdenes con 20 números únicos**, y el ciclo
   de reintentos con un fallo real → DLQ → orden `failed` (la compra sigue válida).

### Por dónde seguir (en este orden)

**El roadmap sigue sin pendientes salvo el 15, diferido a propósito**, y `planning/21` cerró
las dos deudas que dejaba `planning/20` (recuperar desde la DLQ y recoger huérfanos). Lo que
queda son deudas menores, todas en `docs/06` §Paso 12:

1. **Deuda menor viva**: `planning/13` (la cookie es una decisión para SPA: un cliente móvil
   no está cubierto), `planning/17` §17.4, `planning/19` §19.4 (EF loguea a Error sus fallos
   de conexión — se deja a propósito), `planning/21` §21.6 (nadie **alerta** cuando la DLQ
   crece: hay que mirar).
2. **Lo grande que falta es dominio, no infraestructura**: `Payments` y `Shipping` son los
   contextos acotados previstos y no existen. `Ordering` dejó el molde hecho.
3. `planning/15` (partir en proyectos) sigue **diferido a propósito**. La señal para
   retomarlo está en `docs/06`.

⚠️ **Colas huérfanas en el broker de desarrollo**: al cambiar `RetryDelaySeconds` quedan
colas `…retry.<N>s` de plazos anteriores. Están vacías y **ya no reciben nada** (no tienen
binding), pero se ven en la UI. Se borran a mano cuando estorben.

### ⚠️ Decisiones que esperan al owner (bloquean, nadie más puede tomarlas)
| | |
|---|---|
| 🔴 **Token de GitHub en `.git/config`** | Un PAT en texto plano en el remoto. **Revocarlo.** Ver §6.bis |
| **Licencia de AutoMapper** | La 15.1.1 exige licencia comercial en producción. Es lo único que queda de `planning/12` |
| **Umbral de QuestPDF** | Community es gratis —también comercialmente— **por debajo de 1 M USD** de ingresos brutos anuales, con 90 días de transición. No es «gratis para siempre» |
| **Subir la CI** | El workflow está commiteado pero **sin push**; falta activarlo y proteger la rama |
| **Migrar a `net10.0`** | Hoy resuelto instalando el runtime 9 |
| **IP del host en `appsettings.Development.json`** | Quedó `192.168.3.82` (la LAN del autor), que **cambia con DHCP**, en un fichero commiteado. `172.17.0.1` —la puerta del bridge— es estable desde el dev container. Decidir cuál se deja |

---

## 1. Qué es esto

**`ApiEcommerce`** — API REST de e-commerce en **ASP.NET Core 9 + EF Core 9 + SQL Server**,
proyecto único, backend-only.

Es un **proyecto de aprendizaje con estándar de producción**. El autor viene de
**Spring Boot / Java** y usa el repo para aprender .NET replicando una arquitectura que ya
domina: DI por constructor, repository + service detrás de interfaces, genéricos base para
el CRUD, y un handler global que traduce excepciones de dominio a HTTP.

**El objetivo declarado** (owner, 2026-08-30): dejarlo *prod-ready para apps medianas y
medianas-grandes* **sobre esta arquitectura**, antes de saltar a Clean Architecture,
use cases, DDD, hexagonal o CQRS. Esas vendrán después y hay señales concretas para saber
cuándo tocan (`docs/06-estado-y-roadmap.md`, paso 9).

El repo nació siguiendo un curso, cuyo código está en `AGENTS/__ref__/01/code`
(ramas `fin-seccion-3` … `fin-seccion-15`). **Ese código es material de origen, no
autoridad**: se trajo la *feature*, nunca el *código*.

---

## 2. Cómo se levanta

```sh
dotnet build                                          # build de la solución (API + tests)
dotnet test tests/ApiEcommerce.Tests                  # 274 tests, ~85 s
dotnet watch run --urls "http://0.0.0.0:8021"         # dev
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
```

⚠️ **La app NO arranca sin user-secrets.** El repo no lleva ningún secreto, tampoco los de
desarrollo. Tras clonar hay que poner tres valores (`ConnectionStrings:ConexionSql`,
`Jwt:SecretKey`, `Seed:AdminPassword`); los comandos exactos están en `README_init.md`.
`UserSecretsId` = `apiecommerce-dev-2026`. Sin la clave JWT, el arranque falla con
`OptionsValidationException` — es lo correcto, no un fallo de configuración del entorno.

**CI**: `.github/workflows/ci.yml` corre build (`-warnaserror`) + los 274 tests en cada
push y PR, con SQL Server y Redis como `services` del runner, y construye el `Dockerfile`.

### Los tests (paso 11, fases 1–5; falta la 6, CI)

`tests/ApiEcommerce.Tests` — xunit + Moq, TFM `net9.0`, estructura espejo del slicing.
Los de integración levantan la API entera con `WebApplicationFactory` contra **SQL Server
y Redis reales**, con recursos propios que **no pisan los de desarrollo**:

| | |
|---|---|
| Base de datos | `ApiEcommerceNET8_Tests` — **se borra y se migra en cada corrida** |
| Prefijo en Redis | `apiecommerce-tests:` |
| Broker | desactivado (`RabbitMq:ConnectionString` vacío) |

Los endpoints salen de `TEST_SQL_HOST` / `TEST_SQL_PORT` / `TEST_SQL_PASSWORD` /
`TEST_REDIS`, con los valores locales por defecto: así el mismo código sirve en el dev
container (`172.17.0.1`) y en CI (`localhost`).

**No hay Testcontainers**: no hay Docker dentro del dev container. Entra en la fase 6 (CI),
donde el runner sí lo tiene.

Swagger (solo en Development): `http://localhost:8021/swagger/index.html` — un documento
por versión de API. Sondas: `GET /health` (liveness) y `GET /health/ready` (SQL + Redis +
backlog del outbox).

### Infraestructura — **fuera de este repo**

Vive en un `docker-compose` central del autor (`~/Documents/code/000_infra`), compartido
con otros proyectos, en la red `backend`:

| Servicio | Host desde la app | Estado |
|---|---|---|
| `sqlserver_ecommerce` | `192.168.3.82,1434` · BD `ApiEcommerceNET8` | ✅ arriba |
| `redis_generic` | `192.168.3.82:6999` | ✅ arriba · `maxmemory 0` / `noeviction` |
| `rabbitmq_generic` | `192.168.3.82:5672` | ✅ arriba (desde 2026-09-05; UI en `:15672`, guest/guest) |

⚠️ **Dos direcciones valen para lo mismo**: `192.168.3.82` es la IP LAN del host
(2026-09-06; cambia con DHCP) y `172.17.0.1` es la puerta del bridge de Docker, estable
desde dentro del dev container. Las dos responden. Los **tests** siguen usando
`172.17.0.1` por defecto (`TEST_SQL_HOST` / `TEST_REDIS` lo sobreescriben).
⚠️ Ese Redis es **compartido con otros proyectos**: si alguien le pone `allkeys-lru`, la
idempotencia se rompe **en silencio** (las claves se desalojarían).

El bloque de RabbitMQ está en **`docker-compose.fragment.yml`** (raíz del repo), ya pegado
en el compose central. Si algún día no está, la app arranca igual y los eventos se acumulan
en `OutboxMessages` — es el diseño, no un fallo.

**La API en contenedor** tiene su propio **`docker-compose.prod.yml`** (raíz del repo):
declara *solo* la app y se engancha a la red `backend` de aquel compose como **externa**
(`name: ${INFRA_NETWORK:-000_infra_backend}` — compose prefija con el nombre del proyecto).
Secretos por `.env` (plantilla en `.env.example`).

⚠️ **Dentro del dev container NO hay Docker.** Ni el `Dockerfile` ni los composes se pueden
construir o levantar desde aquí: los ejecuta el owner en el host. Y ojo con las dos formas
de direccionar lo mismo: desde el dev container es `172.17.0.1` + puerto **publicado**
(1434 / 6999 / 5672); desde dentro de la red `backend` es nombre de servicio + puerto
**interno** (`sqlserver_ecommerce,1433`, `redis_generic:6379`, `rabbitmq_generic:5672`).

**Credenciales de desarrollo**: `admin` / `Admin123!` (rol `admin`, sembrado por
`DataSeeder` cuando `Seed:Enabled` es `true`).

### Runtime — el dev container solo trae el SDK 10

El SDK 10 compila `net9.0`, pero **ejecutar** necesita el runtime 9 instalado aparte. Los
runtimes de .NET conviven *side-by-side* y cada app carga el de su TFM, así que instalar el
9 **no cambia nada** para lo que apunta a `net10.0` fuera de este proyecto. Si se recrea el
contenedor, hay que repetirlo:

```sh
curl -sSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh && chmod +x /tmp/dotnet-install.sh
sudo /tmp/dotnet-install.sh --channel 9.0 --runtime aspnetcore --install-dir /usr/share/dotnet --no-path
dotnet --list-runtimes   # deben salir 9.0.x y 10.0.x
```

⚠️ El `cp: '/usr/share/dotnet/dotnet': Text file busy` que aparece si hay una app corriendo
es inocuo: es el *muxer*, y el del 10 sirve para ambos.

---

## 3. Mapa del repo

**Vertical slicing por contexto acotado** (`rules.md` §4):

```
Features/
  Catalog/       <- contexto: categorias y productos
    Models/ Dtos/ Repository/ Service/ Mapping/ Controllers/
    Events/                     <- ProductPurchased (vocabulario del catalogo)
    Messaging/                  <- ProductPurchasedConsumer (quien REACCIONA)
    CatalogExtensions.cs        <- AddCatalogFeature(): el DI del slice
  Accounts/      <- contexto: identidad, JWT, autorizacion
    Models/ Dtos/ Service/ Controllers/
    JwtOptions.cs ConfigureJwtBearerOptions.cs AccountsExtensions.cs
  Ordering/      <- contexto: ordenes y su comprobante
    Models/ Dtos/ Repository/ Service/ Controllers/
    Ports/                      <- ICatalogGateway: LO UNICO del slice que sabe de Catalog
    Events/                     <- OrderPlaced
    Messaging/                  <- OrderPlacedConsumer + IReceiptGenerator (el EFECTO)
    Documents/                  <- QuestPdfReceiptRenderer (la libreria de PDF, aislada)
    OrderingExtensions.cs
Shared/          <- transversal, de ningun dominio
  Persistence/   IEntity, IBaseRepository, BaseRepository, PersistenceExtensions
  Crud/          ICrudService, CrudService, IEntityRules, NoEntityRules
  Caching/ Db/ Idempotency/ Paging/ Mapping/ Auth/
  Storage/       imagenes PUBLICAS (dentro de wwwroot, las sirve UseStaticFiles)
  Documents/     documentos PRIVADOS (FUERA de wwwroot, los sirve un endpoint) <- NO es lo mismo
  Messaging/     MECANISMO: outbox, RabbitMq/, IDomainEvent, EventConsumer<,>,
                 AddEventConsumer<T>(config, EventSubscription)
                 (los eventos, sus colas y sus consumidores viven en el SLICE que los emite)
  Http/          GlobalExceptionHandler, Swagger, CORS, rate limit + Health/
  DependencyInjection/   composition root (NO registra nada)
Data/            AppDbContext + DataSeeder
Exceptions/      jerarquia AppException (dominio -> HTTP)
Migrations/      EF Core (13 aplicadas)
AGENTS/          docs/ features/ planning/ context/ + memory.md progress.md rules.md
notes.md         <- el log de aprendizaje del autor. MUY importante, ver §6
```

⚠️ **Un slice es un contexto acotado, NO una entidad.** `UnitOfMeasurement` o `ProductTag`
irían dentro de `Catalog/`; no crean carpeta propia.

⚠️ **`Shared/` no nombra tipos de `Features/`.** La única excepción legítima es el
composition root, que por definición conoce ambos lados. Si algo de `Shared/` necesita un
tipo de un slice, o el tipo está en el sitio equivocado, o hay que **invertir** la
dependencia y pasárselo desde el composition root (es lo que se hizo con el ensamblado
que escanea AutoMapper).

---

## 4. Lo esencial de la arquitectura (el detalle en `docs/`)

**Controller → Service (DTO) → Repository (entidad) → `AppDbContext`.**

La regla que lo sostiene: **se hereda para reutilizar mecanismo, se compone para
reutilizar política.** `BaseRepository<T>` se hereda; el CRUD de servicio se **compone**
(`ICrudService`) y las reglas de negocio viven fuera, en `IEntityRules`.

Piezas que conviene conocer antes de tocar nada:

- **`CrudService<TEntity,TDto,TCreateDto,TUpdateDto>`** — `sealed`, DTO-facing (no lleva
  `TEntity` en la interfaz, para que el controller no pueda ver la entidad).
- **`CachedCategoryService`** — **decorador** que añade cache-aside a `ICategoryService`.
  `CategoryService` no sabe que existe cache.
- **`ITransactionRunner`** (`Shared/Db/`) — la unidad transaccional de negocio. **No uses
  `[Transactional]` para esto**: `ActionExecutionDelegate` no es reentrante y con
  reintentos puede ejecutar la acción dos veces.
- **`IEventOutbox`** — encola el evento en la MISMA transacción; `OutboxPublisher` lo
  publica después.
- **DI**: cada slice registra lo suyo en su `XxxExtensions.cs`;
  `Shared/DependencyInjection/` solo compone (`AddSharedInfrastructure` / `AddFeatures` /
  `AddWebApi`). Añadir un slice = crear la carpeta y una línea en `AddFeatures()`.

---

## 5. Trampas ya pisadas — no volver a caer

| Trampa | Qué pasa |
|---|---|
| `AGENTS/**` compilándose | MSBuild glob-ea todo `.cs` bajo el csproj. Ya está excluido; **no quitar** esa exclusión. |
| Varios `[Authorize]` | Se **combinan (AND)**, no se sobreescriben. Solo `[AllowAnonymous]` gana. El requisito débil va en la clase. |
| `CreatedAtRoute` con ruta versionada | Necesita el parámetro `version` explícito, o el `Location` falla con un 500 sin relación. |
| Controller sin `[ApiVersion]` | 404. Los de infraestructura llevan `[ApiVersionNeutral]`. |
| Serilog | Lee la sección **`Serilog`**, NO la sección `Logging` del scaffold. |
| `nvarchar(max)` | **No es indexable** en SQL Server. Índice único ⇒ `[MaxLength]` en la entidad. |
| `ExecuteUpdateAsync` | No dispara la auditoría, no toca el change tracker, y `DateTime.Now` dentro se traduce a `GETDATE()`. |
| `[Required]` en options | Se valida al leer `.Value`, aunque la feature esté apagada. Regla condicional ⇒ `.Validate(...)`. |
| Arrays de configuración | Se **fusionan** por índice, no se reemplazan. |
| `dotnet ef --no-build` | Usa el ensamblado viejo → `PendingModelChangesWarning` sin sentido. |
| Editar con scripts | Una sustitución global se cuela en los **bloques comentados** de aprendizaje. Ya pasó dos veces. |
| Rate limiter propio | 100 req/min global y 10/min en `auth`. Al hacer pruebas de carga te limita **a ti**: reinicia el proceso para resetear la ventana. |
| Lockout de Identity | 5 logins fallidos bloquean la cuenta 5 min. Probar con un usuario nuevo, no con el de siempre. |
| **Un `BackgroundService` y `OperationCanceledException`** | `catch (Exception ex) when (ex is not OperationCanceledException)` **deja escapar** una OCE que no venga del `stoppingToken` (cancelación de un `SqlCommand`, timeout del cliente AMQP) → con `StopHost` por defecto desde .NET 6, **tumba la API entera**. El patrón correcto es `catch (OCE) when (stoppingToken.IsCancellationRequested) { break; }` y luego `catch (Exception)` **sin filtro**. |
| **Marca de idempotencia y efecto** | Van en la **misma transacción**. Confirmar la marca antes del efecto hacía que, si el efecto fallaba, la reentrega se reconociera como duplicado, se hiciera ack y el mensaje **desapareciera sin procesarse**: los reintentos quedaban inertes. |
| **Publicar por un canal AMQP** | Si el canal no tiene *publisher confirms*, `BasicPublishAsync` vuelve sin excepción aunque el mensaje no llegue a ninguna cola (`mandatory: true` sin handler de retorno lo descarta en silencio). **Nunca hacer ack después de un publish sin confirms.** |
| **Fuga de conexiones a RabbitMQ** | Si la declaración de topología falla, hay que hacer `DisposeAsync` de la conexión local: con `AutomaticRecoveryEnabled` queda viva para siempre. Medido: 22 fallos = 22 conexiones fugadas. |
| **406 PRECONDITION_FAILED ≠ broker caído** | Es una cola que ya existe con otros argumentos (p. ej. cambiar `RabbitMq:RetryDelaySeconds`, que fija el `x-message-ttl`). Es un error de configuración **permanente**; tratarlo como caída deja la mensajería abajo reintentando en bucle. |
| **`Sequence` NO garantiza el orden** | El `IDENTITY` se asigna al `INSERT` y la fila se ve al `COMMIT`: una transacción lenta con secuencia menor puede confirmar después. Es determinista y repetible, no ordenado. |
| **Serilog no renderiza el `LogContext`** | La plantilla de fábrica **no** imprime las propiedades: el `CorrelationId` se empujaba bien y no salía en ninguna línea. Hace falta `outputTemplate`. ⚠️ Y declarar el sink en código **y** en `Serilog:WriteTo` no sustituye: **duplica** cada línea. |
| **Redis caído no puede ser `Unhealthy`** | El health check con el `failureStatus` por defecto devolvía **503** en `/health/ready` con Redis muerto: como la caída la ven todas las réplicas, el orquestador las sacaba **todas** de rotación por una dependencia opcional. Va en `Degraded`. |
| **Tests: `UseSetting`, NO `ConfigureAppConfiguration`** | Varias piezas (`AddDistributedCaching`, `AddMessaging`, `AddHealthProbes`) leen la config **eager** para decidir QUÉ implementación registran, y eso pasa **antes** de que corran esos callbacks. Con `ConfigureAppConfiguration` el host arrancaba con `NoIdempotencyStore` y **los tests de idempotencia pasaban sin probar nada**. `TestHostGuardTests` es la red. |
| **Tests de integración: sin paralelismo** | Todos van en `[Collection(IntegrationCollection.Name)]`, **incluidos los que levantan su propio host** (degradación, arranque): comparten base de datos y en paralelo chocan con `Database ... already exists`. Pasaban en aislado y fallaban en la suite completa. |
| `tests/**` en el `.csproj` | Está **excluido** igual que `AGENTS/**`. Sin eso el glob del SDK Web compila los tests dentro de la API (xunit y Moq en la imagen de producción, y referencia circular). **No quitar.** |
| FluentAssertions | **No se usa**, aunque la skill la pida: desde la v8 exige licencia comercial. `Assert` de xunit basta. |
| **Runtime .NET** | El SDK del dev container es **10.0.400** y el proyecto es `net9.0`: compila, pero necesita el **runtime 9** instalado o no arranca. Ya está (9.0.19 y 10.0.11 conviven). Si el contenedor se recrea, se pierde: reinstalar con el comando de abajo. |
| `pkill -f ApiEcommerce` | **Se mata a sí mismo**: el cwd y la propia línea de comando contienen esa cadena. Guardar el PID (`echo $! > api.pid`) y `kill` por PID. Ya ha pasado dos veces. |
| Arrancar la app | `dotnet run` deja un proceso hijo que no muere con el padre. Mejor `dotnet bin/Debug/net9.0/ApiEcommerce.dll`, que sí da el PID real. |
| **Un segundo consumidor de eventos** | El publicador usa `mandatory: true` con confirms: un evento que **no encaja con ninguna cola** vuelve como **312 NO_ROUTE**, el outbox lo cuenta como intento fallido y se agota. La compra funciona y el consumidor no se entera **nunca**. Cada slice declara su `EventSubscription`; sin ella no hay binding. |
| **Redeclarar una cola existente** | Cambiar cualquier argumento (`x-dead-letter-exchange`, `x-message-ttl`) da **406 PRECONDITION_FAILED** y deja la mensajería abajo. Por eso la cola del catálogo conserva su DLX heredada y los slices nuevos usan una **por cola** (la heredada es `fanout` y repartiría los muertos de uno a la DLQ del otro). |
| **Comentarios `//` en `Serilog:MinimumLevel:Override`** | Serilog resuelve **cada clave** como nombre de logger: el arranque muere con `No LoggingLevelSwitch has been declared with name "…"`. Van un nivel más arriba. |
| **QuestPDF** | La licencia se declara al **arrancar** o lanza **al generar** (la API arranca sana y los comprobantes fallan uno a uno). En Linux necesita **`libfontconfig1`** en la imagen. Y **no se pide `FontFamily`**: Calibri no existe en Linux; la de QuestPDF va **embebida**. |
| **`Documents:RootPath`** | Es **relativo al content root**. Sin sobreescribirlo, los tests dejarían PDFs dentro del repo. |
| `POST /api/v1/category` | Devuelve **201 sin cuerpo** (`CreatedAtRoute(..., null)`): el id sale de la cabecera `Location`, no del JSON. No es un fallo. |

---

## 6. `notes.md` — cómo se escribe (importa)

`notes.md` (raíz) es el **log de aprendizaje** del autor, no documentación de referencia.
Cada feature nueva se registra ahí como un capítulo, en el estilo exacto que ya usa el
archivo: `## Capítulo`, viñetas anidadas `- --- tema` / `  - -- subtema` / `    - detalle`,
bloques ```sh``` con los comandos reales, y mucha separación entre capítulos. En español.

**Petición literal del owner: «NOOO me llenes de texto innecesario ni de contenido no
útil».** Se escribe solo lo **no obvio**: el gotcha, el porqué, el comando exacto, y —al
portar algo del curso— **qué hacía mal el original**. Ese contraste es lo que más valora.
Los gotchas se marcan con ⚠️. Los números medidos van con su medición.

Los capítulos 30–37 son la referencia de estilo más reciente.

---

## 6.ter Una sola fuente de verdad por dato (regenerado el 2026-09-06)

La documentación se reescribió contra el código porque **`CLAUDE.md` tenía ~20 afirmaciones
falsas** —carpetas que no existían, nombres de método del composition root inventados, «no
hay proyecto de tests» con 261 tests en verde— y ese archivo **se carga en cada sesión**,
así que cada mentira contaminaba todas a la vez.

Para que no vuelva a pasar, cada dato tiene **un solo dueño**:

| Dato | Dueño |
|---|---|
| Arquitectura, convenciones, trampas, comandos | `CLAUDE.md` |
| Las reglas duras, en su forma larga | `rules.md` |
| El razonamiento de diseño (por qué composición, por qué no `virtual`…) | `docs/01`–`05` |
| Qué está hecho y qué falta | `docs/06` + `progress.md` |
| Punto de continuación, decisiones del owner, trampas del entorno | **este archivo** |
| Qué se decidió en una tarea y qué quedó abierto | `planning/NN` |
| El aprendizaje, con el gotcha y la medición | `notes.md` |

⚠️ **Los conteos volátiles (tests, migraciones, endpoints) NO van en `CLAUDE.md`**: viven
aquí y en `progress.md`, que se actualizan en el mismo commit que el código. Si vuelves a
meter un número en `CLAUDE.md`, quedará obsoleto y nadie lo notará.

⚠️ `docs/01`–`05` conservan su razonamiento —que es lo valioso— pero **sus rutas y nombres
de método iban por detrás del código**. Se corrigieron; si vuelves a ver una ruta que no
existe, es esto reapareciendo.

---

## 6.bis ⚠️ Lo más urgente del repo (2026-09-06)

**El remoto de git lleva un token de GitHub en texto plano** dentro de `.git/config`:
`https://ghp_…@github.com/AlexMartin998/dotnet-ecommerce-v1.git`. Aparece en cualquier
`git remote -v`, en logs y en cualquier transcripción. Mientras siga sin revocarse, el
resto del trabajo sobre secretos vale poco:

```sh
# 1) revocarlo en GitHub: Settings -> Developer settings -> Personal access tokens
# 2) quitarlo del remoto y volver a autenticar
git remote set-url origin https://github.com/AlexMartin998/dotnet-ecommerce-v1.git
gh auth login            # o pasar el remoto a SSH
```

---

## 7. Decisiones tomadas que NO hay que reabrir

- **AutoMapper, no Mapster.** El curso migró a Mapster en su sección 15; aquí no, porque
  `docs/` fija AutoMapper *y* porque el curso usó `.TwoWays()` indiscriminado en DTOs de
  escritura, que es justo lo que hace que un update pise `CreatedAt`.
  ⚠️ **Pendiente de decisión del owner**: AutoMapper 15 exige licencia comercial en
  producción (avisa por log al arrancar).
- **Redis en vez de `[ResponseCache]`.** El del curso no invalida, no funciona con
  cabecera `Authorization` y vive en la memoria de un proceso.
- **`RowVersion` NO es para el stock.** Se probó y se midió: rechazaba compras válidas.
  El stock usa un UPDATE condicional atómico.
- **`DateTime.Now` local** (no UTC) en todo el modelo. Es una decisión ya tomada; migrar a
  `DateTimeOffset` está en el roadmap como cambio de todo a la vez.
- **Los secretos de desarrollo van en user-secrets**, no en `appsettings.Development.json`
  (cambiado el 2026-09-06). El fichero sigue commiteado, pero solo con configuración no
  sensible. `rules.md` §5.
- **Testcontainers, no todavía.** Los tests de integración usan la infraestructura del
  host con base y prefijo propios, porque **no hay Docker en el dev container**. No es
  pereza: es la única opción aquí. En CI sí se usarán.
- **La web no es offline-first** — no aplica: esto es una API.

---

## 8. Estado en una línea

Tres contextos acotados con el slice vertical completo: **Catalog** (categorías y
productos), **Accounts** (identidad, sesiones revocables, administración de usuarios) y
**Ordering** (órdenes + comprobante en PDF asíncrono). Auth con Identity+JWT y refresh en
cookie `HttpOnly`, versionado, CORS, cache Redis con decorador, paginación, subida de
imágenes, seeding, rate limiting, Serilog, health checks, protección de carreras,
idempotencia **transaccional** (`ExecutedCommands`, en la misma transacción que el efecto),
outbox + RabbitMQ **con varias colas** —verificado de punta a punta contra un broker real—
y un almacén de documentos privados sustituible (`IDocumentStore`).

**Tests y CI**: `tests/ApiEcommerce.Tests`, **274** (unitarios + integración + concurrencia
+ degradación y arranque) y `.github/workflows/ci.yml`. El roadmap está **sin pendientes
salvo el 15** (partir en proyectos, diferido a propósito); lo que queda son deudas menores,
anotadas en §0.

Ver `progress.md` para el detalle de qué está hecho, qué está a medias y qué falta.
