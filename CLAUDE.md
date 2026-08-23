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

Swagger UI is registered only when `ASPNETCORE_ENVIRONMENT=Development`: `http://localhost:8021/swagger/index.html`. Health probe: `GET /health`.
`Properties/launchSettings.json` defaults to port 5241 (http) / 7223 (https) when run without `--urls`.

Migrations (EF Core, `dotnet-ef` installed globally):

```sh
dotnet ef migrations list
dotnet ef migrations add <Name>
dotnet ef migrations remove          # undo the last *unapplied* migration file
dotnet ef database update
```

There is no test project and no linter configured — `dotnet build` is the only check.

## Database

The connection string `ConnectionStrings:ConexionSql` is hardcoded in `appsettings.json` and points at a SQL Server container reachable at `172.17.0.1,1434` (Docker bridge gateway) with database `ApiEcommerceNET8`. The API will not start meaningfully without that container running.

Note: the globally installed `dotnet ef` tooling is 10.x while the project targets net9.0 with EF Core 9.0.9. It works, but a mismatch here is the first thing to check if migration commands behave oddly.

## Architecture

Request flow: **Controller → Service (DTO in/out) → Repository (entity in/out) → `AppDbContext`**. Entities never leave the service layer; controllers only ever see DTOs from `Models/Dtos/`.

The load-bearing rule is **inherit to reuse mechanism, compose to reuse policy**:

- **Repository — inheritance.** `IBaseRepository<T>` / `BaseRepository<T>` is CRUD over `DbSet<T>`, constrained to `where T : class, IEntity`, calling `SaveChangesAsync()` inside every write (there is no unit-of-work). Its methods are deliberately **not `virtual`**: the base is pure mechanism with no business decisions, and subclasses (`CategoryRepository : BaseRepository<Category>(db), ICategoryRepository`) only *add* domain queries.
- **Service — composition.** `Service/Crud/` holds `ICrudService<TDto, TCreateDto, TUpdateDto>` (DTO-facing, deliberately no `TEntity`, so controllers cannot see entities) and the `sealed` `CrudService<TEntity, TDto, TCreateDto, TUpdateDto>`. Entity services *have* a `CrudService` and delegate the five CRUD methods to it; they do not inherit. Business rules live outside the service in `IEntityRules<TEntity, TCreateDto, TUpdateDto>` (`CategoryRules`, `ProductRules`), whose members are default interface methods so an entity implements only what it needs. `NoEntityRules<,,>` is registered as the open generic default; closed per-entity registrations win over it.

A service may skip delegation for one operation: `ProductService` implements `GetAllAsync`/`GetByIdAsync` itself, using repository methods that `.Include(p => p.Category)`, because the generic CRUD cannot populate `ProductDto.CategoryName`.

`Models/IEntity.cs` defines the marker interfaces `IEntity` (`int Id`) and `IAuditable : IEntity` (`CreatedAt`/`UpdatedAt`). `AppDbContext.SaveChangesAsync` is overridden to stamp them automatically — do not assign timestamps by hand.

**State of the code:** Category and Product both have the full vertical slice (model → repo → rules → service → controller), all registered in `Shared/DependencyInjection/ServiceCollectionExtensions.cs`. `IGenericService<T>` / `GenericService<T>` and `Mapping/MappingProfile.cs` are retired — commented out in place as a learning record, per the convention above.

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

- `notes.md` — the author's running Spanish-language notes on .NET/EF/AutoMapper concepts and the step-by-step history of how the project was built.
- `README_init.md` — the scaffold-from-scratch cheatsheet (`dotnet new webapi`, package installs, JWT/SQL Server setup).
