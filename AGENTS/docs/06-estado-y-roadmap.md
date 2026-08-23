# 06 — Estado actual y roadmap

Foto del repo al **2026-08-23**, después de cerrar los pasos 1–5 del roadmap
anterior (andamiaje transversal + slice de Product + composición en la capa de
servicio).

## Estado por componente

| Componente | Estado | Nota |
| --- | --- | --- |
| `AppDbContext` + migraciones | ✅ | 5 migraciones aplicadas; auditoría automática en `SaveChangesAsync` |
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
| `HealthController` | ✅ | `GET /health` |
| Jerarquía `AppException` | ✅ | + `Unauthorized` (401), `Forbidden` (403), `Validation` (422) |
| Handler global de errores | ✅ | `GlobalExceptionHandler` + `ProblemDetails` (RFC 7807) |
| `TransactionalAttribute` | ✅ | aplicado a `POST /api/product/buy` |
| AutoMapper | ✅ | `CategoryProfile`, `ProductProfile`; `MappingProfile` retirado (comentado) |
| Validación de DTOs | ✅ | DataAnnotations completas en los 5 DTOs de entrada |
| Registro de DI | ✅ | extraído a `Shared/DependencyInjection/ServiceCollectionExtensions.cs` |
| `CancellationToken` extremo a extremo | ✅ | controller → servicio → repositorio → EF |
| Autenticación / JWT | ❌ | previsto en `README_init.md`, sin empezar |
| Tests | ❌ | sin proyecto de pruebas |
| Logging estructurado | ⚠️ | el handler global loguea con scope; el resto es el default |
| Paginación en listados | ❌ | `GetAllAsync` sigue trayendo la tabla entera |

## Lo que se verificó

`dotnet build` limpio (0 warnings) y smoke test de 18 casos contra la base real,
cubriendo el camino feliz y **cada excepción de dominio**: 409 por nombre
duplicado, 409 por SKU duplicado, 409 por borrar categoría con productos, 409 por
stock insuficiente, 404 por id/SKU inexistente, 400 por FK inexistente, 400 de
DataAnnotations, 200 con `[]` en búsqueda sin resultados, y PATCH parcial que no
pisa los campos que no vienen.

## Roadmap

### Paso 7 — Proyecto de tests `ApiEcommerce.Tests`

Es el siguiente paso natural y ahora es barato: la composición dejó las reglas
como unidades aisladas.

1. `dotnet new xunit -o ../ApiEcommerce.Tests` + referencia al proyecto.
2. `CategoryRules` / `ProductRules` con un `IXRepository` mockeado (Moq): un test
   por excepción de dominio que la regla puede lanzar. **Empezar por aquí**: es
   donde vive la lógica y no necesita ni DbContext ni mapper.
3. `CrudService` con `IBaseRepository<T>` + `IMapper` mockeados: verificar que
   `GetByIdAsync` lanza `NotFoundAppException`, que las reglas se invocan **antes**
   de escribir, y que el update mapea sobre la entidad rastreada.
4. Perfiles de AutoMapper: `configuration.AssertConfigurationIsValid()` y un test
   del PATCH parcial (que `s.X ?? d.X` no pisa lo que no viene).
5. Patrón AAA, camino feliz y camino de error.

### Paso 8 — Paginación

`PagedResult<T> { Items, Page, PageSize, Total }` en `Shared/`, un
`GetPagedAsync(page, pageSize, ct)` en `BaseRepository` (aprovechando
`ApplyDefaultOrder`) y endpoints `GET /api/x?page=&pageSize=`. Hoy los listados
traen la tabla entera.

### Paso 9 — JWT + Identity

`README_init.md` ya tiene el cheatsheet. Activa de verdad el 401/403 del handler
global (`UnauthorizedAppException` / `ForbiddenAppException` ya existen y ya están
mapeadas). Al hacerlo, **mover las credenciales** de `appsettings.json` a
user-secrets o variables de entorno.

### Paso 10 — Más allá

- **Logging estructurado** con scopes por request.
- **`DbUpdateException` → `ConflictAppException`** para violaciones de índice
  único, como red de seguridad ante carreras (hoy la unicidad se comprueba antes
  de escribir, pero dos requests simultáneos pueden colarse).
- **`DateTime.Now` → `DateTimeOffset`/UTC**, si algún día el proyecto sale de una
  sola zona horaria. Es una migración de todo a la vez: entidades, DTOs y datos.
- **Concurrencia optimista** (`[Timestamp] byte[] RowVersion`) en `Product`: la
  compra hace read-then-write y hoy nada impide vender el mismo stock dos veces.

## Cómo mantener este documento

Al terminar un paso, actualizar la tabla de estado en el mismo commit que el
código. Un roadmap desactualizado es peor que no tenerlo: hace que quien lo lee
(persona o agente) trabaje sobre una foto falsa del repo.
