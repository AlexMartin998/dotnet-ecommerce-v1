# 02 — Capa Repository

Equivalente a `JpaRepository<T, ID>` de Spring Data: una base genérica con el
CRUD resuelto, más una interfaz por entidad donde viven las consultas propias
del dominio.

## Aquí sí se hereda (y es a propósito)

La capa de servicio usa composición (`03-service.md`), pero `Repository/` hereda
de `BaseRepository<T>`. No es una incoherencia: son dos situaciones distintas.

- `BaseRepository<T>` es **mecanismo puro**: traduce intención de persistencia a
  EF Core y no toma ni una decisión de negocio. No hay política que sustituir.
- Las subclases **solo agregan** consultas (`NameExistsAsync`, `SearchProductAsync`).
  Nunca cambian el CRUD.
- Por eso **los métodos de la base no son `virtual`**: nadie puede alterar en
  silencio la semántica documentada más abajo. Si una entidad necesita otra cosa,
  agrega un método nuevo con otro nombre — como `ProductRepository.GetAllWithCategoryAsync`.

La regla general del proyecto: **heredar para reutilizar mecanismo, componer para
reutilizar política.**

## Estructura

```
IBaseRepository<T> where T : class, IEntity     ← contrato CRUD genérico
   └── BaseRepository<T>                        ← implementación sobre DbSet<T>
          ├── ICategoryRepository : IBaseRepository<Category>   (+ NameExistsAsync, HasProductsAsync)
          │      └── CategoryRepository : BaseRepository<Category>(db), ICategoryRepository
          └── IProductRepository : IBaseRepository<Product>     (+ lecturas con Include, SKU, búsqueda)
                 └── ProductRepository : BaseRepository<Product>(db), IProductRepository
```

Reglas:

- **Por defecto**, la interfaz de repositorio hereda de `IBaseRepository<T>`, y nunca
  redeclara `GetByIdAsync` / `GetAllAsync` en la interfaz específica.
- **Por defecto**, la implementación hereda de `BaseRepository<T>` con constructor primario:
  `public class XRepository(AppDbContext db) : BaseRepository<X>(db), IXRepository`.
- ⚠️ **Y hay excepciones legítimas, que se justifican por escrito.** `IOrderRepository` y
  `IRefreshTokenRepository` **no heredan**: de las cinco operaciones del CRUD genérico no
  les vale ninguna tal cual —una orden no se actualiza ni se borra, se coloca y cambia de
  estado— y heredarlas sería exponer cinco métodos que nadie debe llamar. Heredar por
  costumbre es peor que no heredar.
- `_db`, `_dbSet`, `Query()` y `ApplyDefaultOrder()` son `protected` en la base;
  los métodos específicos los usan directamente.
- Un método en la interfaz específica solo se justifica si expresa una **consulta
  de dominio**. Si es CRUD puro, ya está en la base.

## Contrato de `IBaseRepository<T>`

```csharp
Task<T?>              GetByIdAsync(int id, CancellationToken ct = default);
Task<IEnumerable<T>>  GetAllAsync(CancellationToken ct = default);
Task<T>               AddAsync(T entity, CancellationToken ct = default);
Task<T>               UpdateAsync(T entity, CancellationToken ct = default);
Task                  DeleteAsync(int id, CancellationToken ct = default);
Task<bool>            ExistsAsync(int id, CancellationToken ct = default);
Task<bool>            ExistsByFieldAsync(string fieldName, string value, int? excludeId = null, CancellationToken ct = default);
```

Semántica esperada — no cambiar sin actualizar este documento:

| Método | Devuelve cuando no existe | Lanza |
| --- | --- | --- |
| `GetByIdAsync` | `null` | nunca |
| `GetAllAsync` | colección vacía | nunca |
| `DeleteAsync` | no-op silencioso | nunca |
| `ExistsAsync` | `false` | nunca |

> **El repositorio nunca lanza excepciones de negocio.** Es el contrato que hace
> que el servicio sea el único dueño de las reglas.

## La restricción `where T : class, IEntity`

`Shared/Persistence/IEntity.cs` define dos interfaces marcadoras:

```csharp
public interface IEntity                 { int Id { get; } }
public interface IAuditable : IEntity    { DateTime CreatedAt { get; set; } DateTime? UpdatedAt { get; set; } }
```

Las implementan `Category` y `Product`, y resuelven tres cosas de golpe:

- `CrudService` lee `entity.Id` **sin reflexión**.
- `BaseRepository.GetAllAsync` puede **ordenar por `CreatedAt` de forma genérica**.
- Los genéricos dejan de aceptar cualquier `class`.

## Detalles de implementación a respetar

### Lecturas con `AsNoTracking`

`Query()` devuelve `AsNoTracking()` por defecto; `Query(tracking: true)` para lo
que se va a modificar. Excepción explícita: **`GetByIdAsync` usa `FindAsync`, que
sí rastrea**, y eso es deliberado, porque el update del servicio hace
`_mapper.Map(dto, existing)` sobre esa instancia rastreada. No "optimizar"
`GetByIdAsync` a `AsNoTracking`: rompería el update.

Cuando una lectura necesita una navegación (`ProductDto.CategoryName`), el
repositorio expone un método propio con `.Include(...)` y el servicio lo usa en
lugar del `GetAllAsync` genérico. El genérico no sabe nada de navegaciones y
nunca va a saberlo.

### Orden de los listados

`ApplyDefaultOrder` ordena por `CreatedAt` descendente si `T` es `IAuditable`, y
por `Id` descendente si no. Usa `EF.Property<DateTime>(e, "CreatedAt")` y no un
cast a `IAuditable`, porque un cast dentro del árbol de expresión **no es
traducible a SQL**.

### Comparaciones case-insensitive

Deben ser **traducibles a SQL**. `StringComparison.OrdinalIgnoreCase` no lo es
en EF Core y explota en runtime. El patrón aceptado es normalizar a variable
local y comparar con `.ToLower().Trim()`:

```csharp
var normalized = name.Trim().ToLower();
return await _db.Categories.AnyAsync(c => c.Name.ToLower().Trim() == normalized, ct);
```

`ExistsByFieldAsync` hace lo mismo de forma genérica, reflejando sobre `_db.Model`
para validar que el campo existe y es `string`. Es útil para unicidad genérica,
pero **si la entidad tiene un método dedicado (`NameExistsAsync`, `SkuExistsAsync`),
se usa el dedicado**: es más rápido y no depende de un string mágico.

El parámetro `excludeId` existe para la unicidad **en el update**: sin él, un
PATCH que reenvía el mismo nombre choca consigo mismo y devuelve un 409 falso.

### `ExistsAsync` y `UpdateAsync`

Dos detalles que parecen menores y no lo son:

- `ExistsAsync` usa `AnyAsync` (un `SELECT 1`), **no `FindAsync`**: `FindAsync`
  materializaría la entidad entera y la metería en el change tracker.
- `UpdateAsync` solo llama a `_dbSet.Update(entity)` **si la entidad viene
  desconectada**. En el camino normal (`GetByIdAsync` + `Map` encima) ya está
  rastreada, y llamar a `Update()` marcaría todas las columnas como modificadas,
  generando un `UPDATE` de la fila completa en vez de solo los campos que cambiaron.

### Escrituras y transacciones

Hoy **no hay unit of work**: cada `AddAsync` / `UpdateAsync` / `DeleteAsync`
llama a `SaveChangesAsync()` internamente. Consecuencias:

- Una operación de servicio que toque dos repositorios **no es atómica** por
  defecto.
- **Regla:** la unidad transaccional la abre el **servicio** con `ITransactionRunner`, no
  el controller. Dentro de ese delegado, los `SaveChanges` intermedios quedan en la misma
  transacción y hacen rollback juntos.
- ⚠️ **`[Transactional]` existe pero NO se usa en ninguna acción**, y no es un olvido: con
  `EnableRetryOnFailure`, EF exige la transacción dentro de `strategy.ExecuteAsync(...)` y
  la estrategia **reejecuta el delegado**. Un `ActionExecutionDelegate` **no es
  reentrante**: invocarlo dos veces ejecutaría la acción dos veces. Una lambda de servicio
  sí se puede repetir. Además la transacción es política de **negocio**, no de HTTP.
- ⚠️ El contrato de `ITransactionRunner` es que **la operación debe ser replayable**: puede
  ejecutarse más de una vez ante un fallo transitorio, así que no puede dar por buena
  ninguna lectura del intento anterior.

La evolución hacia un `IUnitOfWork` explícito ya ocurrió a medias y por partes:
`IBaseRepository.SaveChangesAsync()` + `ITransactionRunner` son esa costura, y
`OrderRepository.Add()` deliberadamente **no** guarda — manda la transacción de negocio.

### Timestamps

`CreatedAt` / `UpdatedAt` usan `DateTime.Now` (hora local, no UTC). Es una
decisión ya tomada: **mantener la consistencia**. Si algún día se migra a
`DateTime.UtcNow`, se migra todo a la vez y se documenta aquí.

**Ya no se asignan a mano.** `AppDbContext.SaveChangesAsync` está sobreescrito y
estampa `CreatedAt` en las entradas `Added` y `UpdatedAt` en las `Modified`
(además de marcar `CreatedAt` como no modificado, para que un update no lo pise).
Es el equivalente a `@EnableJpaAuditing` de Spring. Un repositorio que asigne
`UpdatedAt` a mano está duplicando trabajo.

## Registro en DI

En `Shared/DependencyInjection/ServiceCollectionExtensions.cs` → `AddCatalogFeature()` / `AddOrderingFeature()`,
lifetime **Scoped** (igual que `AppDbContext`):

```csharp
services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));
services.AddScoped<ICategoryRepository, CategoryRepository>();
services.AddScoped<IProductRepository, ProductRepository>();
```

Nunca `Singleton` para algo que depende de `AppDbContext`: el `DbContext` es
Scoped y capturarlo en un singleton corrompe el change tracker.
