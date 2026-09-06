# AGENTS/memory.md — Norte de arranque de sesión

> **Léeme primero, siempre.** Esto es lo que un agente necesita saber en los primeros 30
> segundos de una sesión: qué es este repo, cómo se levanta, dónde está cada cosa y qué
> trampas ya hemos pisado. El detalle vive en `rules.md` (reglas duras),
> `docs/` (arquitectura, MANDA) y `progress.md` (avance y pendientes).

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
dotnet test tests/ApiEcommerce.Tests                  # 153 tests, ~18 s
dotnet watch run --urls "http://0.0.0.0:8021"         # dev
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
```

⚠️ **La app NO arranca sin user-secrets.** El repo no lleva ningún secreto, tampoco los de
desarrollo. Tras clonar hay que poner tres valores (`ConnectionStrings:ConexionSql`,
`Jwt:SecretKey`, `Seed:AdminPassword`); los comandos exactos están en `README_init.md`.
`UserSecretsId` = `apiecommerce-dev-2026`. Sin la clave JWT, el arranque falla con
`OptionsValidationException` — es lo correcto, no un fallo de configuración del entorno.

**CI**: `.github/workflows/ci.yml` corre build (`-warnaserror`) + los 153 tests en cada
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
| `sqlserver_ecommerce` | `172.17.0.1,1434` · BD `ApiEcommerceNET8` | ✅ arriba |
| `redis_generic` | `172.17.0.1:6999` | ✅ arriba |
| `rabbitmq_generic` | `172.17.0.1:5672` | ✅ arriba (desde 2026-09-05; UI en `:15672`, guest/guest) |

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
Shared/          <- transversal, de ningun dominio
  Persistence/   IEntity, IBaseRepository, BaseRepository, PersistenceExtensions
  Crud/          ICrudService, CrudService, IEntityRules, NoEntityRules
  Caching/ Db/ Idempotency/ Paging/ Storage/ Mapping/ Auth/
  Messaging/     MECANISMO: outbox, RabbitMq/, IDomainEvent, AddEventConsumer<T>
                 (los eventos y sus consumidores viven en el SLICE que los emite)
  Http/          GlobalExceptionHandler, Swagger, CORS, rate limit + Health/
  DependencyInjection/   composition root (NO registra nada)
Data/            AppDbContext + DataSeeder
Exceptions/      jerarquia AppException (dominio -> HTTP)
Migrations/      EF Core (8 aplicadas)
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
| **Tests: `UseSetting`, NO `ConfigureAppConfiguration`** | Varias piezas (`AddDistributedCaching`, `AddMessaging`, `AddHealthProbes`) leen la config **eager** para decidir QUÉ implementación registran, y eso pasa **antes** de que corran esos callbacks. Con `ConfigureAppConfiguration` el host arrancaba con `NoIdempotencyStore` y **los tests de idempotencia pasaban sin probar nada**. `TestHostGuardTests` es la red. |
| **Tests de integración: sin paralelismo** | Todos van en `[Collection(IntegrationCollection.Name)]`, **incluidos los que levantan su propio host** (degradación, arranque): comparten base de datos y en paralelo chocan con `Database ... already exists`. Pasaban en aislado y fallaban en la suite completa. |
| `tests/**` en el `.csproj` | Está **excluido** igual que `AGENTS/**`. Sin eso el glob del SDK Web compila los tests dentro de la API (xunit y Moq en la imagen de producción, y referencia circular). **No quitar.** |
| FluentAssertions | **No se usa**, aunque la skill la pida: desde la v8 exige licencia comercial. `Assert` de xunit basta. |
| **Runtime .NET** | El SDK del dev container es **10.0.400** y el proyecto es `net9.0`: compila, pero necesita el **runtime 9** instalado o no arranca. Ya está (9.0.19 y 10.0.11 conviven). Si el contenedor se recrea, se pierde: reinstalar con el comando de abajo. |
| `pkill -f ApiEcommerce` | **Se mata a sí mismo**: el cwd y la propia línea de comando contienen esa cadena. Guardar el PID (`echo $! > api.pid`) y `kill` por PID. Ya ha pasado dos veces. |
| Arrancar la app | `dotnet run` deja un proceso hijo que no muere con el padre. Mejor `dotnet bin/Debug/net9.0/ApiEcommerce.dll`, que sí da el PID real. |
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

Capítulos 15–20 son la referencia de estilo más reciente.

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

Category y Product tienen el slice vertical completo. Hay auth con Identity+JWT,
versionado, CORS, cache Redis con decorador, paginación, subida de imágenes, seeding,
rate limiting, Serilog, health checks, protección de carreras, idempotencia y outbox +
RabbitMQ — este último **verificado de punta a punta contra un broker real** el
2026-09-05, incluidos deduplicación y DLQ.

**Ya hay tests**: `tests/ApiEcommerce.Tests`, **153** (unitarios + integración +
concurrencia + degradación y arranque), verdes en dos corridas seguidas. Falta **CI**
(fase 6 del `planning/11`): la red existe, pero nadie obliga a que esté tendida antes de
un merge.

Ver `progress.md` para el detalle de qué está hecho, qué está a medias y qué falta.
