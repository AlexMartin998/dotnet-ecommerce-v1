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

## Estructura de carpetas

```
ApiEcommerce/
├── Controllers/          # capa HTTP. Solo DTOs.
├── Service/              # lógica de negocio. DTO in / DTO out.
│   ├── Crud/                   # CRUD reutilizable POR COMPOSICIÓN
│   │   ├── ICrudService.cs         contrato DTO-facing (sin TEntity)
│   │   ├── CrudService.cs          implementación sealed
│   │   ├── IEntityRules.cs         reglas por entidad
│   │   └── NoEntityRules.cs        "sin reglas" (genérico abierto en DI)
│   ├── Auth/                   # IAuthService/AuthService, IJwtTokenService/JwtTokenService
│   ├── ICategoryService.cs / CategoryService.cs / CategoryRules.cs
│   ├── CachedCategoryService.cs   # decorador de cache sobre ICategoryService
│   └── IProductService.cs  / ProductService.cs  / ProductRules.cs
├── Repository/           # acceso a datos. Entity in / Entity out. AQUÍ SÍ SE HEREDA.
│   ├── IBaseRepository.cs
│   ├── BaseRepository.cs
│   ├── ICategoryRepository.cs / CategoryRepository.cs
│   └── IProductRepository.cs  / ProductRepository.cs
├── Models/               # entidades EF Core
│   ├── IEntity.cs        # IEntity / IAuditable (marcadoras)
│   └── Dtos/             # contratos de la API
├── Mapping/              # un Profile de AutoMapper por entidad
├── Exceptions/           # jerarquía AppException (dominio → HTTP)
├── Shared/
│   ├── Auth/             # Roles, JwtOptions, SeedOptions, extensiones de ClaimsPrincipal
│   ├── Caching/          # ICacheService, RedisCacheService, NoCacheService, CacheKeys
│   ├── Db/               # TransactionalAttribute, helpers de EF
│   ├── Http/             # GlobalExceptionHandler, ConfigureSwaggerOptions, CORS, rate limit
│   ├── Paging/           # PagedResult<T>
│   ├── Storage/          # IFileStorage, LocalFileStorage, FileUpload
│   └── DependencyInjection/  # composition root: AddApplication/AddInfrastructure/AddWebApi
├── Data/                 # AppDbContext + DataSeeder
├── wwwroot/              # archivos estáticos (imágenes de producto)
├── Migrations/           # EF Core
└── AGENTS/docs/          # estos lineamientos
```

**El registro de DI de cada feature vive en la carpeta del feature**, en un
`XExtensions.cs` (`Data/PersistenceExtensions.cs`,
`Shared/Caching/CachingExtensions.cs`, `Service/Auth/AuthExtensions.cs`…).
`Shared/DependencyInjection/` solo los compone. Ver `05-convenciones.md`.

**Namespace = ruta de carpeta**, con `ApiEcommerce` como raíz y namespaces
file-scoped: `Repository/CategoryRepository.cs` → `namespace ApiEcommerce.Repository;`.
Nótese que las carpetas de capa van en **singular** (`Service`, `Repository`),
que es la convención ya establecida en el repo — no renombrar a plural.

## Slice vertical

La unidad de trabajo es el **slice vertical por entidad**. Agregar una entidad
significa crear, en este orden:

1. `Models/X.cs` — entidad + DataAnnotations + índices, implementando `IAuditable`.
2. `dotnet ef migrations add ...` — migración.
3. `Models/Dtos/XDto.cs`, `CreateXDto.cs`, `UpdateXDto.cs`.
4. `Mapping/XProfile.cs`.
5. `Repository/IXRepository.cs` + `XRepository.cs` (heredan de `BaseRepository<X>`).
6. `Service/XRules.cs` — las reglas de negocio de la entidad (o ninguna).
7. `Service/IXService.cs` + `XService.cs` (**componen** `ICrudService<...>`).
8. `Controllers/XController.cs`.
9. **Registrar repositorio, reglas, `CrudService` cerrado y servicio** en el
   `Add…` de la carpeta correspondiente (`Repository/RepositoryExtensions.cs` y
   `Service/ApplicationServiceExtensions.cs`). Este es el paso que más se olvida.
   El composition root no se toca: ya llama a esos dos.

`Category` es el slice de referencia: cuando dudes de una convención, mira cómo
está hecha ahí y replícala.

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
