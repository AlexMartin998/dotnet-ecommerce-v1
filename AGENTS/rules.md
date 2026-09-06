# AGENTS/rules.md — Reglas duras (ApiEcommerce, backend-only)

> Reglas no negociables para cualquier agente (Claude Code u otro) que trabaje en este
> repositorio: una **API REST backend-only** en ASP.NET Core 9 + EF Core + SQL Server.
> Se asumen **siempre** verdaderas. Si una instrucción del usuario contradice una regla,
> **gana el usuario**.
>
> Este es un **proyecto de aprendizaje con estándar de producción**: el autor viene de
> Spring Boot y usa el repo para aprender .NET replicando una arquitectura que ya conoce.
> Eso cambia cómo se escribe el código: **el porqué de cada decisión importa tanto como
> la decisión**.

---

## 1. Idioma — comentarios y documentación en **español**, identificadores en **inglés**

- **Comentarios, XML docs (`///`), `AGENTS/**` y `notes.md`: español.** Es el idioma del
  autor y de todas sus notas.
- **Identificadores en inglés**: clases, métodos, propiedades, rutas, nombres de tabla.
  `ProductService.BuyAsync`, no `ServicioProducto.ComprarAsync`.
- **Mensajes de excepción de dominio en inglés**: viajan al cliente en `ProblemDetails`
  y son parte del contrato de la API (`"Insufficient stock for SKU 'X'"`).
- **Mensajes de commit en español.**

### 1.1 Los comentarios explican el PORQUÉ, no el qué

Un comentario que repite lo que dice el código es ruido. Los que valen son los que
registran **la decisión y su alternativa descartada**:

```csharp
// RowVersion (concurrencia optimista) sirve para EDITAR una entidad. Aplicado a un
// contador con mucha contención rechaza compras válidas al agotar los reintentos:
// medido, 15 compras sobre stock 10 daban 5x200 + 5x409. Por eso el stock usa un
// UPDATE condicional atómico y no esto.
```

### 1.2 Los bloques comentados de implementaciones anteriores **NO se borran**

Son el registro de aprendizaje del autor. Solo se borran si él lo pide explícitamente.
⚠️ **Cuidado al editar con scripts**: una sustitución global puede colarse dentro de un
bloque comentado y romperlo (ya pasó dos veces: un `using` sin comentar en
`ProductRepository.cs` y un `[AllowAnonymous]` dentro del bloque legacy de
`CategoryController.cs`).

---

## 2. `AGENTS/docs/` manda sobre el código y sobre el curso de referencia

`AGENTS/docs/` describe el **diseño objetivo**, no lo que hoy existe. Cuando el código
discrepa, **se migra el código**. El índice es
[`AGENTS/docs/README.md`](docs/README.md) y dice qué documento aplica a qué tarea.

- **`AGENTS/docs/06-estado-y-roadmap.md` se actualiza en el MISMO commit** que el cambio
  que describe. Un roadmap desactualizado hace que quien lo lee —persona o agente—
  trabaje sobre una foto falsa del repo.
- `AGENTS/__ref__/01/code` es el repo del curso (ramas `fin-seccion-*`). Es **material de
  origen, no autoridad**: se trae la *feature*, nunca el *código*. Y se anota en
  `notes.md` qué hacía mal el original.
- Cuando la skill `dotnet-best-practices` y estos documentos discrepen, **manda
  `AGENTS/docs/`**.

---

## 3. Arquitectura — el flujo no se salta y las capas no se filtran

**Controller → Service (DTO in/out) → Repository (entidad in/out) → `AppDbContext`.**

| Capa | Entra | Sale | Prohibido |
|---|---|---|---|
| Controller | DTO, ruta, query | `IActionResult` con DTOs | `try/catch` de negocio, `IMapper`, repositorios, LINQ, entidades en el body |
| Service | DTOs | DTOs | devolver entidades, conocer `HttpContext`/`StatusCodes`, usar `AppDbContext`, lanzar excepciones BCL como señal |
| Repository | entidades, ids | entidades, `bool`, colecciones | conocer DTOs o `IMapper`, lanzar excepciones de negocio, aplicar reglas |

**La regla que sostiene todo:** *se hereda para reutilizar **mecanismo**, se compone para
reutilizar **política**.* `BaseRepository<T>` se hereda (mecanismo puro, métodos **no
`virtual`**); el CRUD de servicio se **compone** (`ICrudService`) y las reglas viven fuera,
en `IEntityRules`.

- **El repositorio devuelve `null`/`false`/vacío**; quien decide que "no encontrado" es un
  404 es el servicio.
- **El servicio lanza `AppException`**; quien la traduce a HTTP es
  `GlobalExceptionHandler`. Cero `try/catch` de negocio en controllers.
- **La transacción es política de NEGOCIO**, no de HTTP: va en el servicio con
  `ITransactionRunner`, no en un atributo del controller. (`[Transactional]` no puede dar
  una unidad reintentable: `ActionExecutionDelegate` no es reentrante.)
- `Catalog` es el **slice de referencia** y `Category` la entidad de referencia dentro
  de él. Ante la duda, copia su forma.

---

## 4. Organización — **vertical slicing por contexto acotado**

El código se organiza por **dominio**, no por capa técnica. `Features/<Contexto>/` contiene
**todo lo suyo**, y dentro se mantienen las carpetas de capa de siempre:

```
Features/
  Catalog/                    <- contexto acotado
    Models/                   Category.cs, Product.cs
    Dtos/                     CategoryDto, CreateProductDto, BuyProductDto...
    Repository/               ICategoryRepository + impl, IProductRepository + impl
    Service/                  ICategoryService + impl, CategoryRules, ProductService...
    Mapping/                  CategoryProfile, ProductProfile
    Controllers/              CategoryController, ProductController
    CatalogExtensions.cs      <- el DI del slice: AddCatalogFeature()
  Accounts/                   <- identidad, JWT, autorizacion
    Models/ Dtos/ Service/ Controllers/ + AccountsExtensions.cs
Shared/                       <- transversal, de ningun dominio
  Persistence/ Crud/ Caching/ Db/ Http/ Idempotency/ Messaging/ Paging/ Storage/ Mapping/
  DependencyInjection/        <- composition root, NO registra nada
```

### 4.1 Un slice es un **contexto acotado**, no una entidad

`UnitOfMeasurement`, `ProductTag` o `Brand` **no crean un slice**: son parte del
vocabulario del catálogo y viven dentro de `Catalog/`. La pregunta para decidir es de DDD:

> ¿esto tiene su propio lenguaje ubicuo y sus propias invariantes, o es parte del
> vocabulario de otro contexto?

Un slice por entidad reproduce exactamente la dispersión que el slicing venía a quitar,
solo que con más carpetas.

Contextos previstos según crezca: `Catalog`, `Accounts`, `Ordering`, `Payments`, `Shipping`.

### 4.2 Cada slice registra lo suyo

- Un `XxxExtensions.cs` en la raíz del slice con `AddXxxFeature()`: sus repositorios, sus
  reglas, sus `CrudService` cerrados y sus servicios.
- **Añadir un slice = crear su carpeta y una línea en `AddFeatures()`.**
- Lo que es de **todos** (los genéricos abiertos `IBaseRepository<>` y `NoEntityRules<,,>`,
  la cache, el storage, la mensajería) vive en `Shared/` y se registra una sola vez.

### 4.3 Composition root

`Shared/DependencyInjection/ServiceCollectionExtensions.cs` **no registra nada**: compone
en tres bloques que declaran la dirección **Web → Features → Shared**.

```csharp
builder.Services
    .AddSharedInfrastructure(builder.Configuration)  // EF Core, Redis, disco, mensajeria
    .AddFeatures(builder.Configuration)              // un bloque por contexto acotado
    .AddWebApi(builder.Configuration);               // superficie HTTP
```

En un proyecto único esa dirección es una **convención, no una frontera que imponga el
compilador**; pero son las costuras exactas por donde se parte la solución en proyectos.

### 4.4 Lifetimes

- **Scoped** para todo lo que dependa de `AppDbContext`.
- **Singleton** para lo que no guarda estado por request y solo depende de singletons.
- **Un servicio se registra con el MISMO lifetime en todas sus ramas de registro** — una
  rama `Scoped` y otra `Singleton` es una mina que solo estalla en el entorno que sí tiene
  la infraestructura.
- Los servicios reciben **`IOptions<T>`, nunca `IConfiguration`**. Para configurar opciones
  del framework desde las nuestras: `IConfigureOptions<T>` / `IConfigureNamedOptions<T>`.

## 5. Configuración y secretos

- **Los secretos NO se commitean. Ninguno, tampoco los de desarrollo.**
  `appsettings.json` y `appsettings.Development.json` llevan **solo configuración no
  sensible** y ambos se commitean. Los tres valores sensibles —cadena de conexión, clave
  JWT y contraseña del admin sembrado— viven en **user-secrets**
  (`dotnet user-secrets set "Jwt:SecretKey" "…"`), y en cualquier otro entorno en variables
  de entorno (`Jwt__SecretKey`, doble guion bajo por cada `:`).
  > Cambiado el 2026-09-06. Antes los de desarrollo vivían en
  > `appsettings.Development.json`, que **está commiteado**: cómodo en local, pero
  > convierte cada clon del repo en una copia de las credenciales. Los comandos de puesta
  > en marcha están en `README_init.md`.
- Toda sección se enlaza a una **clase tipada con DataAnnotations** y se valida con
  `AddOptions<T>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`.
- ⚠️ **Una regla condicional no se expresa con un atributo.** `[Required]` se evalúa
  siempre que alguien lea `.Value`. Una opción obligatoria *solo si otra bandera está
  activa* va en `.Validate(...)`. (Con `[Required]` en `SeedOptions.AdminPassword`, un
  despliegue con el seeding apagado moría en **crash-loop**.)
- ⚠️ **Los arrays de configuración se FUSIONAN por índice, no se reemplazan.** Definir
  `Cors__AllowedOrigins__0` por entorno **no** borra los índices de `appsettings.json`.
  Las listas dependientes del entorno van vacías en el fichero base.

---

## 6. Errores — el dominio lanza intención, la infraestructura decide el código

- Toda excepción nueva **hereda de `AppException`** y fija su `Code` y su
  `HttpStatusCode` en el constructor.
- **400 vs 409**: si el request es inválido en sí mismo, 400; si es válido pero choca con
  el estado de la base, 409. **401 vs 403**: "no sé quién eres" vs "sé quién eres y no
  puedes".
- Un listado sin resultados es **200 con `[]`**, nunca 404. Una página fuera de rango
  también.
- ⚠️ **No hagas pattern matching sobre la FORMA del anidamiento de excepciones.**
  `SaveChangesAsync` envuelve el `SqlException` en `DbUpdateException`; `ExecuteUpdate*`
  lo lanza **desnudo**. Se recorre la cadena de `InnerException`.
- La red de seguridad para excepciones BCL es **estrecha a propósito**:
  `InvalidOperationException` **no** se mapea (EF la usa para errores de programación, y
  mapearla filtraba mensajes internos del ORM al cliente).

---

## 7. Concurrencia — lo que no se ve probando de uno en uno

Estas reglas nacen de bugs **medidos**, no de teoría:

| Problema | Herramienta correcta |
|---|---|
| Contador con contención (stock, saldo, cupos) | **UPDATE condicional atómico** (`ExecuteUpdateAsync` con `WHERE`) |
| Editar una entidad (dos admins a la vez) | **Concurrencia optimista** (`[Timestamp] RowVersion`) |
| Unicidad de un campo | **Índice único en la BASE** (la regla aplicativa es solo el mensaje bonito) |
| Doble submit / reintento del cliente | **`Idempotency-Key` + `SET NX` en Redis** |
| Escribir en BD y en el broker | **Outbox transaccional** |

- ⚠️ `nvarchar(max)` **no es indexable** en SQL Server: un campo con índice único necesita
  `[MaxLength]` **en la entidad**, no solo en el DTO.
- ⚠️ `ExecuteUpdateAsync` **no pasa por `SaveChangesAsync`**: la auditoría automática no
  se dispara (hay que poner `UpdatedAt` a mano) y **no toca el change tracker** (hay que
  releer). Y `DateTime.Now` **dentro** del árbol de expresión se traduce a `GETDATE()`,
  o sea el reloj del servidor SQL: hay que capturarlo en una variable local.

---

## 8. Todo lo que degrada, degrada **en abierto** — y de forma coherente

Cache, idempotencia y mensajería son **optimizaciones**, no dependencias duras:

- Redis caído → se sirve de la base; la idempotencia se salta. **Nunca un 500.**
- RabbitMQ caído → la compra se completa y el evento queda en el outbox.
- **La decisión debe ser la MISMA en todas las implementaciones de una interfaz.**
  Que `RedisCacheService` fallara en abierto y `RedisIdempotencyStore` en cerrado hacía
  que un corte de Redis devolviera 500 por una compra ya cobrada.
- Si algún día algo debe fallar en cerrado (pagos), se invierte **explícitamente y en los
  dos sitios a la vez**, y se devuelve 503, no un 500 accidental.
- Un `BackgroundService` que lanza **muere y no vuelve** — y desde .NET 6 el default es
  `StopHost`, así que **tumba la API entera**. El bucle va siempre en `try/catch`, y
  `OperationCanceledException` solo se trata como apagado si el `stoppingToken` está
  cancelado.

---

## 9. Skill obligatoria: `dotnet-best-practices`

Instalada en `.agents/skills/`, enlazada a `.claude/skills/`.

- **Se invoca siempre** que se escriba o revise código C# de estructura (servicios,
  repositorios, DI, configuración, manejo de errores).
- **Precedencia: `AGENTS/docs/` gana.** La skill es genérica y trae secciones que **no
  aplican** a este proyecto: Semantic Kernel, `ResourceManager` para localización, patrón
  Command Handler, y MSTest+FluentAssertions como framework obligatorio.
- Lo que sí aplica y refuerza: constructores primarios para DI, `async`/`await` en toda
  I/O, prefijo `I` en interfaces, lifetimes explícitos, SOLID, evitar duplicación.
- **Matiz importante**: la skill dice "evitar duplicación mediante **clases base**". Aquí
  eso solo vale para `BaseRepository<T>`; en la capa de servicio la reutilización es por
  **composición** (ver §3).
- **Para trabajo grande, revisión multiagente.** Es como se encontraron los bugs que el
  build y el smoke test no veían: crash-loop en Production, `curl` ausente en la imagen
  `aspnet`, `[Transactional]` ejecutando la acción dos veces. Un agente por eje
  (concurrencia / mensajería / infraestructura) con la instrucción de **verificar
  ejecutando**, no de opinar.

---

## 10. Flujo obligatorio: spec → planning → código

Antes de escribir código para una tarea no trivial:

1. **Spec como `.feature` de Gherkin**: keywords en **inglés** (`Feature`, `Scenario`,
   `Given`, `When`, `Then`, `And`), descripciones en **español**.
2. **Planning como checklist `.md`** con pasos técnicos ejecutables.
3. Solo entonces, implementar.

### Ubicación de artefactos

Por tarea, un slug numerado `NN_<slug>`:

```
AGENTS/
  features/    NN_<slug>.feature   ← contrato Gherkin
  planning/    NN_<slug>.md        ← checklist técnico
  context/     notas de arquitectura y diseño puntuales
  docs/        ← lineamientos de arquitectura (MANDAN, ver §2)
  memory.md    ← norte de arranque de sesión (leer PRIMERO, siempre)
  progress.md  ← bitácora de avance y pendientes
  rules.md     ← este archivo
```

Los `.feature` **no son decorativos**: son la especificación de los tests del paso 7 del
roadmap. Cada `Scenario` debería poder convertirse en un test.

---

## 11. Verificación — el suelo innegociable

- **`dotnet build` limpio (0 warnings, 0 errores)** tras cada cambio estructural.
- **`dotnet test tests/ApiEcommerce.Tests` en verde** (153 tests). Los de integración
  necesitan SQL Server y Redis arriba; usan base y prefijo propios y no tocan los de
  desarrollo. GitHub Actions corre ambos en cada push y PR (`.github/workflows/ci.yml`).
- **Lo que toque concurrencia, dependencias externas o el arranque se prueba de verdad**,
  no solo compilando. Concretamente:
  - concurrencia → peticiones **simultáneas** (`for … & done; wait`), no secuenciales;
  - degradación → con la dependencia **caída**;
  - arranque → con `ASPNETCORE_ENVIRONMENT=Production` y la configuración mínima.
- ⚠️ `dotnet ef … --no-build` usa el **ensamblado ya compilado**: si cambiaste el modelo y
  no compilaste, sale un `PendingModelChangesWarning` que no tiene sentido.
- ⚠️ El `dotnet ef` global es **10.x** y el proyecto es **EF Core 9.0.9**. Funciona, pero
  es lo primero que hay que mirar si una migración se comporta raro.
- **Revisar siempre la migración generada** antes de aplicarla: EF a veces propone un
  drop/recreate que pierde datos.

---

## 12. Versionado — el agente commitea, **nunca** hace push

**Práctica establecida en este repo** (a diferencia del repo de frontend del autor, donde
el agente no commitea):

- El agente **sí** hace `git commit`, con mensaje descriptivo en español que explique el
  **porqué** y liste lo verificado.
- El agente **nunca** hace `git push` ni toca ramas remotas.
- Se trabaja en `dev`, **nunca directamente en `main`**.
- Nada destructivo ni hacia fuera sin confirmación explícita.

> ⚠️ **Esta es la única regla que conviene que el owner confirme.** Si prefiere el modelo
> del otro repo —agente deja los cambios en el árbol de trabajo y él revisa y commitea—,
> es cambiar este párrafo y nada más.

---

## 13. Alcance — backend-only, y la infraestructura vive **fuera** de este repo

- Este repo es **solo API**. No hay ni habrá frontend, ni vistas MVC, ni Razor Pages.
- **La infraestructura NO se versiona aquí.** SQL Server, Redis y RabbitMQ viven en un
  `docker-compose` central del autor (`~/Documents/code/000_infra`), compartido con otros
  proyectos. Este repo aporta **`docker-compose.fragment.yml`**: bloques para *pegar* en
  ese compose, nunca un compose completo que compita con él.
- El `Dockerfile` sí vive aquí: es de esta aplicación.
- **Las migraciones se aplican al arrancar** (`MigrateAsync`), porque la imagen de runtime
  no lleva SDK ni `dotnet-ef`.
