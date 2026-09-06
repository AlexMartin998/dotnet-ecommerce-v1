# 00 — Arquitectura objetivo (ApiEcommerce)

> Documento raíz de los lineamientos. Define **a dónde va** el proyecto, no
> necesariamente lo que hoy existe. Cuando código y documento discrepen, el
> documento manda y el código se migra (ver `06-estado-y-roadmap.md`).

## Idea central

`ApiEcommerce` replica, sobre ASP.NET Core 9, la arquitectura en capas típica de
**Spring Boot**: inyección de dependencias por constructor, repositorios y
servicios detrás de interfaces, clases genéricas base que centralizan el CRUD, y
un manejador global de errores que traduce excepciones de dominio a códigos HTTP.

El objetivo no es "hacer .NET idiomático puro", es **poder razonar el proyecto
con el modelo mental de Spring** sin pelearse con el framework. Donde .NET tiene
un mecanismo equivalente y mejor integrado, se usa el de .NET (ej. `IExceptionHandler`
en vez de inventar un middleware a mano).

## Equivalencias Spring Boot ↔ ASP.NET Core

| Spring Boot | ApiEcommerce (.NET) | Estado |
| --- | --- | --- |
| `@RestController` | `[ApiController]` + `ControllerBase` | ✅ |
| `@RequestMapping("/api/x")` | `[Route("api/[controller]")]` | ✅ |
| `@Service` | `Service/XService.cs` + `IXService` | ✅ parcial |
| `@Repository` | `Repository/XRepository.cs` + `IXRepository` | ✅ |
| `JpaRepository<T, ID>` | `IBaseRepository<T>` / `BaseRepository<T>` | ✅ |
| Servicio base reutilizable | `ICrudService<...>` / `CrudService<...>` **por composición** | ✅ |
| Reglas de negocio por entidad | `IEntityRules<...>` (`CategoryRules`, `ProductRules`) | ✅ |
| `@Autowired` / constructor injection | DI de `Program.cs` + constructor primario | ✅ |
| `@Configuration` / `@Bean` | un `XExtensions.cs` por feature + composition root | ✅ |
| `ModelMapper` / MapStruct | AutoMapper `Profile` por entidad | ✅ |
| `@ControllerAdvice` + `@ExceptionHandler` | `IExceptionHandler` global | ✅ |
| `ResponseStatusException` | `AppException` con `Code` + `HttpStatusCode` | ✅ |
| `@Transactional` | `Shared/Db/TransactionalAttribute` | ✅ (en `POST /api/v1/product/buy`) |
| Spring Security (`SecurityFilterChain`) | `AddIdentityAndJwt` + JWT bearer | ✅ |
| `UserDetailsService` + `PasswordEncoder` | `UserManager<ApplicationUser>` | ✅ |
| `AuthenticationManager` | `SignInManager<ApplicationUser>` | ✅ |
| `@PreAuthorize("hasRole('ADMIN')")` | `[Authorize(Roles = Roles.Admin)]` | ✅ |
| `@ConfigurationProperties` + `@Validated` | `AddOptions<T>().ValidateDataAnnotations().ValidateOnStart()` | ✅ |
| `@Cacheable` / `@CacheEvict` | decorador `CachedCategoryService` sobre `ICacheService` | ✅ |
| Spring Data `Pageable` / `Page<T>` | `PageQuery` / `PagedResult<T>` | ✅ |
| `@Profile("dev")` + `data.sql` | `Data/DataSeeder` + `Seed:Enabled` | ✅ |
| `@Valid` + Bean Validation | DataAnnotations en los Create/Update DTO | ✅ |
| `application.yml` | `appsettings.json` | ✅ |
| Hibernate / JPA | EF Core + `AppDbContext` | ✅ |
| `@EnableJpaAuditing` | override de `SaveChangesAsync` + `IAuditable` | ✅ |
| Flyway / Liquibase | EF Core Migrations | ✅ |

## Flujo de una petición

```
HTTP
 │
 ▼
Controller ──── DTO in ────► Service ──── Entity ────► Repository ────► AppDbContext ──► SQL Server
     ▲                          │                          │
     └──── DTO out ─────────────┘                          │
                                                     SaveChangesAsync()
     ▲
     │ AppException  ──────────────────────────────────────┘
     │
GlobalExceptionHandler ──► ProblemDetails (400/401/403/404/409/422/500)
```

Reglas duras del flujo:

1. **Las entidades no salen de la capa de servicio.** El controller solo conoce
   tipos de `Models/Dtos/`. Si un controller necesita `using ApiEcommerce.Models;`
   para algo que no sea un enum, está mal diseñado.
2. **El repositorio no conoce DTOs.** Recibe y devuelve entidades, punto.
3. **El repositorio no lanza excepciones de negocio.** Devuelve `null`,
   `false` o colección vacía; quien decide que "no encontrado" es un 404 es el
   servicio.
4. **El controller no tiene `try/catch` de negocio.** Cualquier `catch` de
   `KeyNotFoundException` / `InvalidOperationException` en un controller es
   deuda técnica: eso lo resuelve el handler global.
5. **El controller no tiene reglas de negocio.** Solo: validar `ModelState`,
   delegar al servicio, elegir el `IActionResult` del camino feliz.
6. **Se hereda para reutilizar mecanismo, se compone para reutilizar política.**
   `BaseRepository<T>` (mecanismo, sin decisiones) se hereda; el CRUD de servicio
   (que orquesta reglas) se compone. Ver `02-repository.md` y `03-service.md`.

## Estructura de carpetas — vertical slicing por contexto acotado

El código se organiza por **dominio**, no por capa técnica. Cada carpeta de `Features/` es
un **contexto acotado** con todo lo suyo dentro, y dentro se mantienen las carpetas de capa
de siempre:

```
ApiEcommerce/
├── Features/                 # los contextos acotados
│   ├── Catalog/                    categorías y productos
│   │   ├── Models/                 Category.cs, Product.cs
│   │   ├── Dtos/                   contratos de la API de este contexto
│   │   ├── Repository/             IXRepository + impl (heredan de BaseRepository<T>)
│   │   ├── Service/                IXService + impl + XRules
│   │   ├── Mapping/                un Profile de AutoMapper por entidad
│   │   ├── Events/                 eventos de dominio del contexto (ProductPurchased)
│   │   ├── Messaging/              quién REACCIONA a esos eventos (los consumidores)
│   │   ├── Controllers/            capa HTTP. Solo DTOs.
│   │   └── CatalogExtensions.cs    AddCatalogFeature(): el DI del slice
│   └── Accounts/                   identidad, JWT, autorización
│       ├── Models/ Dtos/ Service/ Controllers/
│       ├── JwtOptions.cs  ConfigureJwtBearerOptions.cs
│       └── AccountsExtensions.cs
├── Shared/                   # transversal: de ningún dominio
│   ├── Persistence/          IEntity, IBaseRepository, BaseRepository, ITransactionRunner
│   ├── Crud/                 ICrudService, CrudService, IEntityRules, NoEntityRules
│   ├── Caching/              ICacheService, RedisCacheService, NoCacheService, CacheKeys
│   ├── Idempotency/          IIdempotencyStore, IdempotentAttribute
│   ├── Messaging/            MECANISMO: outbox, RabbitMq/, IDomainEvent, AddEventConsumer<T>
│   ├── Storage/              IFileStorage, LocalFileStorage
│   ├── Paging/               PagedResult<T>, PageQuery
│   ├── Db/                   TransactionalAttribute
│   ├── Mapping/              AddObjectMapping (escanea los Profile de los slices)
│   ├── Auth/                 Roles, SeedOptions, extensiones de ClaimsPrincipal
│   ├── Http/                 GlobalExceptionHandler, Swagger, CORS, rate limit, Health/
│   └── DependencyInjection/  composition root (NO registra nada)
├── Data/                     AppDbContext + DataSeeder
├── Exceptions/               jerarquía AppException (dominio → HTTP)
├── Migrations/               EF Core
├── wwwroot/                  archivos estáticos (imágenes de producto)
└── AGENTS/                   docs/ features/ planning/ context/ + memory/progress/rules
```

**Namespace = ruta de carpeta**, con `ApiEcommerce` como raíz y namespaces file-scoped:
`Features/Catalog/Repository/CategoryRepository.cs` →
`namespace ApiEcommerce.Features.Catalog.Repository;`.

### Un slice es un contexto acotado, no una entidad

`UnitOfMeasurement`, `ProductTag` o `Brand` **no crean un slice**: son parte del vocabulario
del catálogo y viven dentro de `Catalog/`. La pregunta es de DDD: *¿esto tiene su propio
lenguaje ubicuo y sus propias invariantes, o es parte del vocabulario de otro contexto?*
Un slice por entidad reproduce la dispersión que el slicing venía a quitar, con más carpetas.

Contextos previstos según crezca: `Catalog`, `Accounts`, `Ordering`, `Payments`, `Shipping`.

## Slice vertical

La unidad de trabajo es el **contexto acotado**. Agregar una entidad a uno que ya existe
significa crear, dentro de su carpeta y en este orden:

1. `Models/X.cs` — entidad + DataAnnotations + índices, implementando `IAuditable`.
2. `dotnet ef migrations add ...` — migración.
3. `Models/Dtos/XDto.cs`, `CreateXDto.cs`, `UpdateXDto.cs`.
4. `Mapping/XProfile.cs`.
5. `Repository/IXRepository.cs` + `XRepository.cs` (heredan de `BaseRepository<X>`).
6. `Service/XRules.cs` — las reglas de negocio de la entidad (o ninguna).
7. `Service/IXService.cs` + `XService.cs` (**componen** `ICrudService<...>`).
8. `Controllers/XController.cs`.
9. **Registrar repositorio, reglas, `CrudService` cerrado y servicio** en el
   `AddXxxFeature()` de su slice. Este es el paso que más se olvida. Si la entidad entra
   en un contexto que ya existe, el composition root **no se toca**.

`Catalog` es el slice de referencia y `Category` la entidad de referencia dentro de él:
cuando dudes de una convención, mira cómo está hecha ahí y replícala.

## Documentos de esta carpeta

| Doc | Contenido |
| --- | --- |
| `00-arquitectura.md` | este documento: visión, capas, flujo |
| `01-capas-y-contratos.md` | qué puede y no puede hacer cada capa |
| `02-repository.md` | lineamientos del stack de repositorios |
| `03-service.md` | lineamientos del stack de servicios (base genérico) |
| `04-error-handling.md` | jerarquía de excepciones y handler global |
| `05-convenciones.md` | naming, estilo, DI, mapping, migraciones |
| `06-estado-y-roadmap.md` | qué está hecho, qué falta, en qué orden |
