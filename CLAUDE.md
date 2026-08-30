# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

`ApiEcommerce` is a single-project ASP.NET Core 9 Web API (controllers, not minimal APIs) backed by SQL Server via EF Core. It is a learning project: the author is coming from Spring Boot / Java, so the code deliberately mirrors that stack (repository + service + DTO + mapper layering, `[Transactional]` attribute, `MappingProfile` as "ModelMapper"). Comments and `notes.md` are in Spanish, and large commented-out blocks of earlier implementations are kept on purpose as a learning record — do not delete them unless asked.

## Commands

```sh
dotnet restore
dotnet build
dotnet run   --urls "http://0.0.0.0:8021"   # one-shot
dotnet watch run --urls "http://0.0.0.0:8021"   # dev with hot reload
```

Swagger UI is registered only when `ASPNETCORE_ENVIRONMENT=Development`: `http://localhost:8021/swagger/index.html` (one document per API version). Probes: `GET /health` (liveness, no dependencies) and `GET /health/ready` (SQL Server + Redis).
`Properties/launchSettings.json` defaults to port 5241 (http) / 7223 (https) when run without `--urls`.

Migrations (EF Core, `dotnet-ef` installed globally):

```sh
dotnet ef migrations list
dotnet ef migrations add <Name>
dotnet ef migrations remove          # undo the last *unapplied* migration file
dotnet ef database update
```

There is no test project and no linter configured — `dotnet build` is the only check.

## Configuration and infrastructure

Infrastructure lives outside this repo (a `docker compose` in `~/Documents/code/000_infra`):

- **SQL Server** `sqlserver_ecommerce` at `172.17.0.1,1434`, database `ApiEcommerceNET8`. The API will not start meaningfully without it.
- **Redis** `redis_generic` at `172.17.0.1:6999`, used for the distributed cache. Optional: if `Redis:Configuration` is empty the app registers `NoCacheService` and runs without cache.

`appsettings.json` ships with **empty** secrets (`ConnectionStrings:ConexionSql`, `Jwt:SecretKey`); `appsettings.Development.json` carries the dev values. Anything else goes to user-secrets (`dotnet user-secrets set "Jwt:SecretKey" "…"`) or environment variables (`Jwt__SecretKey`).

Every config section binds to a validated options class — `JwtOptions`, `CacheOptions`, `SeedOptions`, `FileStorageOptions`. `JwtOptions` uses `.ValidateOnStart()`, so a missing or short secret fails the boot rather than the first login.

**Serilog reads the `Serilog` section, not `Logging`.** The `Logging` section was removed on purpose so there is only one source of truth for log levels.

Data seeding (`Data/DataSeeder.cs`) is gated by `Seed:Enabled` and creates roles and the admin user through `RoleManager`/`UserManager`, never with direct inserts.

Note: the globally installed `dotnet ef` tooling is 10.x while the project targets net9.0 with EF Core 9.0.9. It works, but a mismatch here is the first thing to check if migration commands behave oddly.

## Architecture

Request flow: **Controller → Service (DTO in/out) → Repository (entity in/out) → `AppDbContext`**. Entities never leave the service layer; controllers only ever see DTOs from `Models/Dtos/`.

The load-bearing rule is **inherit to reuse mechanism, compose to reuse policy**:

- **Repository — inheritance.** `IBaseRepository<T>` / `BaseRepository<T>` is CRUD over `DbSet<T>`, constrained to `where T : class, IEntity`, calling `SaveChangesAsync()` inside every write (there is no unit-of-work). Its methods are deliberately **not `virtual`**: the base is pure mechanism with no business decisions, and subclasses (`CategoryRepository : BaseRepository<Category>(db), ICategoryRepository`) only *add* domain queries.
- **Service — composition.** `Service/Crud/` holds `ICrudService<TDto, TCreateDto, TUpdateDto>` (DTO-facing, deliberately no `TEntity`, so controllers cannot see entities) and the `sealed` `CrudService<TEntity, TDto, TCreateDto, TUpdateDto>`. Entity services *have* a `CrudService` and delegate the five CRUD methods to it; they do not inherit. Business rules live outside the service in `IEntityRules<TEntity, TCreateDto, TUpdateDto>` (`CategoryRules`, `ProductRules`), whose members are default interface methods so an entity implements only what it needs. `NoEntityRules<,,>` is registered as the open generic default; closed per-entity registrations win over it.

A service may skip delegation for one operation: `ProductService` implements `GetAllAsync`/`GetByIdAsync` itself, using repository methods that `.Include(p => p.Category)`, because the generic CRUD cannot populate `ProductDto.CategoryName`.

`Models/IEntity.cs` defines the marker interfaces `IEntity` (`int Id`) and `IAuditable : IEntity` (`CreatedAt`/`UpdatedAt`). `AppDbContext.SaveChangesAsync` is overridden to stamp them automatically — do not assign timestamps by hand.

`Category` is additionally wrapped by a **decorator**, `CachedCategoryService`, which adds Redis cache-aside to the reads and invalidation to the writes. `CategoryService` itself knows nothing about caching; the decision is made once, in the DI registration.

**Authentication** is ASP.NET Core Identity (`ApplicationUser : IdentityUser`, so `AppDbContext : IdentityDbContext<ApplicationUser>`) plus JWT bearer tokens. `ApplicationUser` deliberately does **not** implement `IEntity` — its key is a `string`, and its lifecycle belongs to `UserManager`, not to the generic CRUD. `Service/Auth/` holds `AuthService` (register/login/profile) and `JwtTokenService` (signing, registered as a singleton). Registration always assigns the `user` role; the role is never read from the request body.

**Authorization**: controllers carry `[Authorize]` at class level (authenticated), open public reads with `[AllowAnonymous]`, and restrict writes with `[Authorize(Roles = Roles.Admin)]` per action. Note that multiple `[Authorize]` attributes **combine (AND)** — only `[AllowAnonymous]` overrides the class-level one, which is why the weaker requirement goes on the class.

**Routes are versioned by URL segment**: `[Route("api/v{version:apiVersion}/[controller]")]` + `[ApiVersion("1.0")]`, so everything lives under `/api/v1/…`. `CreatedAtRoute` must pass `version`. Infrastructure controllers (`HealthController`) need `[ApiVersionNeutral]` or they 404. Swagger builds one document per discovered version from `IApiVersionDescriptionProvider` (`Shared/Http/ConfigureSwaggerOptions.cs`), so adding a v2 requires no change to `Program.cs`.

Other cross-cutting pieces: `Shared/Paging/PagedResult<T>` + `PageQuery` (`/paged` endpoints, `pageSize` capped at 100), `Shared/Storage/IFileStorage` (image upload at `POST /api/v1/product/{id}/image`, extension allowlist + magic-byte check, server-generated filename, relative path stored), rate limiting (global per-IP plus a stricter `auth` policy), and Serilog request logging.

**Dependency injection**: each feature registers its own services in an `XExtensions.cs` **inside its own folder** (`Data/PersistenceExtensions.cs`, `Repository/RepositoryExtensions.cs`, `Service/ApplicationServiceExtensions.cs`, `Service/Auth/AuthExtensions.cs`, `Shared/Caching/CachingExtensions.cs`, `Shared/Storage/StorageExtensions.cs`, and four files under `Shared/Http/`). `Shared/DependencyInjection/ServiceCollectionExtensions.cs` is the **composition root** and registers nothing — it only composes those into `AddApplication()` / `AddInfrastructure(config)` / `AddWebApi(config)`, which is all `Program.cs` calls. Those three blocks also declare the dependency direction Web → Infrastructure → Application; in a single project that is a convention, but they are the seams for splitting into projects later.

Lifetimes: **Scoped** for anything depending on `AppDbContext`; **Singleton** for stateless services whose own dependencies are singletons (`IJwtTokenService`, `ICacheService`, `IFileStorage`). A service must use **the same lifetime in every registration branch** — a conditional registration that is Scoped in one branch and Singleton in the other is a captive-dependency landmine that only fires in the environment that has the infrastructure.

Services take **`IOptions<T>`, never `IConfiguration`**. To configure *framework* options from ours, use `IConfigureOptions<T>` / `IConfigureNamedOptions<T>` registered with `services.ConfigureOptions<…>()` — that is what `ConfigureJwtBearerOptions` and `ConfigureSwaggerOptions` do. Reading configuration eagerly *at registration time* is fine only when it decides **which implementation to register** (`AddDistributedCaching`, `AddHealthProbes`).

`AddPersistence` enables `EnableRetryOnFailure`. This is load-bearing: without it `Database.CreateExecutionStrategy()` returns a non-retrying strategy and `[Transactional]` is a no-op. The flip side is that EF then forbids `BeginTransactionAsync` outside `strategy.ExecuteAsync(...)` — `TransactionalAttribute` already wraps it correctly, and any new transaction must too.

**State of the code:** Category and Product both have the full vertical slice (model → repo → rules → service → controller). `IGenericService<T>` / `GenericService<T>` and `Mapping/MappingProfile.cs` are retired — commented out in place as a learning record, per the convention above.

The reference course code under `AGENTS/__ref__/` is **excluded from compilation** by the `.csproj` (`<Compile Remove="AGENTS/**" />`); without that, MSBuild's implicit globbing compiles it and the build fails with duplicate types.

### Error handling

`Exceptions/` defines an `AppException` hierarchy (`NotFoundAppException` 404, `ConflictAppException` 409, `BadOperationAppException` 400, `UnauthorizedAppException` 401, `ForbiddenAppException` 403, `ValidationAppException` 422, `CustomAppException`) carrying a `Code` + `HttpStatusCode`. `Shared/Http/GlobalExceptionHandler.cs` (an `IExceptionHandler`, registered via `AddErrorHandling()` and `app.UseExceptionHandler()` at the top of the pipeline) translates them into RFC 7807 `ProblemDetails`. **Controllers contain no business `try/catch`** — rules throw, the handler maps. The `switch` still has BCL branches (`KeyNotFoundException` → 404 etc.) purely as a safety net.

`Shared/Db/TransactionalAttribute.cs` is an `IAsyncActionFilter` that wraps an action in an EF execution-strategy transaction; it is applied to `POST /api/product/buy`. Because repositories save per operation, it only helps for multi-repository actions.

### Mapping

AutoMapper profiles live in `Mapping/`, one per entity, registered by assembly scan from `CategoryProfile.Assembly`. Two rules that are easy to get wrong:

- Partial PATCH is expressed field by field as `MapFrom((s, d) => s.X ?? d.X)`, **not** `ForAllMembers(o => o.Condition(...))` — `Condition` receives the value already converted to the destination type, so a null `int?` arrives as `0`, is not skipped, and overwrites the field (with `CategoryId` that breaks the FK and returns a 500).
- `CreatedAt`/`UpdatedAt` and the `Product.Category` navigation are always `Ignore()`d on write.

## Conventions

- File-scoped namespaces, `ApiEcommerce.<Folder>` namespace matching the directory.
- Two-space indentation almost everywhere (controllers use four); `Nullable` and `ImplicitUsings` are enabled.
- Primary constructors for repositories (`public class CategoryRepository(AppDbContext db) : ...`), classic constructor injection for services and controllers.
- Reads use `.AsNoTracking()`; list endpoints order by `CreatedAt` descending.
- Timestamps use `DateTime.Now` (local, not UTC) — keep it consistent unless deliberately changing it. They are stamped by `AppDbContext`, never by hand.
- `CancellationToken ct = default` is the last parameter of every async operation, threaded controller → service → repository → EF.
- Route naming: `[Route("api/[controller]")]`, named routes (`[HttpGet("{id:int}", Name = "GetCategory")]`) so `CreatedAtRoute` can reference them, and `[ProducesResponseType]` on every action for Swagger.
- Updates use `PATCH`, not `PUT`, and map the DTO onto the tracked entity (`_mapper.Map(dto, existing)`). Every field in an `UpdateXDto` is nullable, **value types included**.
- Validation lives on the Create/Update DTOs via DataAnnotations; controllers check `ModelState.IsValid` and return `ValidationProblem(ModelState)`.

## Architecture guidelines (read these first)

`AGENTS/docs/` holds the project's architecture guidelines — the **target** design,
which is ahead of the current code. Start at `AGENTS/docs/README.md` (index) and
read the document that matches the task: `01-capas-y-contratos.md` (what belongs
in which layer), `02-repository.md`, `03-service.md`, `04-error-handling.md`,
`05-convenciones.md`, `06-estado-y-roadmap.md` (what's done, what's next). They
are written in Spanish, like the rest of the author's notes.

When the code and those documents disagree, the documents win and the code gets
migrated — `06-estado-y-roadmap.md` tracks the gap and the order to close it.

The repo also has the `dotnet-best-practices` skill installed
(`.agents/skills/`, symlinked into `.claude/skills/`). It is generic .NET/C#
guidance; where it conflicts with `AGENTS/docs/`, `AGENTS/docs/` wins, and its
sections on Semantic Kernel, ResourceManager localization and the Command
Handler pattern do not apply to this project.

## Reference files

- `notes.md` — the author's running Spanish-language notes on .NET/EF/AutoMapper concepts and the step-by-step history of how the project was built. Chapters 1–11 at the end cover the auth/versioning/cache/paging/upload/seeding work and the gotchas hit along the way.
- `README_init.md` — the scaffold-from-scratch cheatsheet (`dotnet new webapi`, package installs, JWT/SQL Server setup).
