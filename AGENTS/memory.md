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
dotnet build                                          # único check automático (no hay tests)
dotnet watch run --urls "http://0.0.0.0:8021"         # dev
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update
```

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
| `rabbitmq_generic` | `172.17.0.1:5672` | ❌ **no existe todavía** |

Para RabbitMQ hay un bloque listo para pegar en ese compose:
**`docker-compose.fragment.yml`** (raíz del repo). Sin él, la app arranca igual y los
eventos se acumulan en la tabla `OutboxMessages` — es el diseño, no un fallo.

**Credenciales de desarrollo**: `admin` / `Admin123!` (rol `admin`, sembrado por
`DataSeeder` cuando `Seed:Enabled` es `true`).

---

## 3. Mapa del repo

**Vertical slicing por contexto acotado** (`rules.md` §4):

```
Features/
  Catalog/       <- contexto: categorias y productos
    Models/ Dtos/ Repository/ Service/ Mapping/ Controllers/
    CatalogExtensions.cs        <- AddCatalogFeature(): el DI del slice
  Accounts/      <- contexto: identidad, JWT, autorizacion
    Models/ Dtos/ Service/ Controllers/
    JwtOptions.cs ConfigureJwtBearerOptions.cs AccountsExtensions.cs
Shared/          <- transversal, de ningun dominio
  Persistence/   IEntity, IBaseRepository, BaseRepository, PersistenceExtensions
  Crud/          ICrudService, CrudService, IEntityRules, NoEntityRules
  Caching/ Db/ Idempotency/ Messaging/ Paging/ Storage/ Mapping/ Auth/
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
- **La web no es offline-first** — no aplica: esto es una API.

---

## 8. Estado en una línea

Category y Product tienen el slice vertical completo. Hay auth con Identity+JWT,
versionado, CORS, cache Redis con decorador, paginación, subida de imágenes, seeding,
rate limiting, Serilog, health checks, protección de carreras, idempotencia y outbox +
RabbitMQ. **No hay tests: es el paso 7 y el siguiente.**

Ver `progress.md` para el detalle de qué está hecho, qué está a medias y qué falta.
