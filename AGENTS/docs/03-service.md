# 03 — Capa Service

La capa que expone DTOs al controller y donde viven las reglas de negocio.

## La decisión de diseño: composición sobre herencia

El CRUD de un servicio es siempre el mismo (mapear, delegar en el repositorio,
lanzar 404 si no existe) y lo que cambia por entidad son las **reglas**. Hay dos
formas de reutilizar ese CRUD, y este proyecto eligió la segunda:

| | Clase base abstracta | Componente compuesto ✅ |
| --- | --- | --- |
| Forma | `CategoryService : BaseService<...>` sobreescribe hooks `OnBefore*` | `CategoryService` **tiene un** `ICrudService` y le delega |
| Reglas | métodos `protected virtual` dentro del servicio | clase propia `CategoryRules : IEntityRules<...>` |
| Probar una regla | hay que construir el servicio entero con todas sus dependencias | `new CategoryRules(repoFalso)` y ya |
| Añadir colaboradores | el constructor pelea con el de la base | uno más en el constructor |
| Saltarse el CRUD | un `override` mal hecho lo consigue en silencio | imposible: `CrudService` es `sealed` |
| Coste | 0 líneas por entidad | **6 reenvíos de una línea por entidad** |

Ese coste de cinco líneas es el único punto a favor de la herencia, y se paga a
cambio de que las reglas sean unidades independientes. **En la capa de servicio
manda la composición.**

> Ojo: esto **no** es "la herencia es mala". En `Repository/` sí se hereda de
> `BaseRepository<T>` a propósito, porque ahí la base es mecanismo puro sin
> ninguna política y las subclases solo *agregan* consultas. Ver `02-repository.md`.

## Estructura

```
Service/
├── Crud/
│   ├── ICrudService.cs      ← contrato CRUD DTO-facing (sin TEntity)
│   ├── CrudService.cs       ← implementación sealed, compuesta
│   ├── IEntityRules.cs      ← reglas por entidad (default interface members)
│   └── NoEntityRules.cs     ← "sin reglas", registrado como genérico abierto
├── ICategoryService.cs      : ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>
├── CategoryService.cs       ← delega en ICrudService
├── CategoryRules.cs         ← nombre único, no borrar con productos
├── IProductService.cs       : ICrudService<...> + búsqueda / categoría / compra
├── ProductService.cs        ← delega escrituras, lecturas propias (Include)
└── ProductRules.cs          ← SKU único, categoría existente
```

### `ICrudService<TDto, TCreateDto, TUpdateDto>`

```csharp
Task<IEnumerable<TDto>> GetAllAsync(CancellationToken ct = default);
Task<TDto>              GetByIdAsync(int id, CancellationToken ct = default);  // lanza NotFound
Task<int>               CreateAsync(TCreateDto dto, CancellationToken ct = default);
Task                    UpdateAsync(int id, TUpdateDto dto, CancellationToken ct = default);
Task                    DeleteAsync(int id, CancellationToken ct = default);
```

**`TEntity` no aparece.** Es lo que permite que `ICategoryService` herede de este
contrato y que un controller que depende de `ICategoryService` no pueda ver
`Category`.

**`GetByIdAsync` lanza `NotFoundAppException`, no devuelve `null`.** Así el 404
sale igual desde cualquier endpoint sin depender de que cada controller se
acuerde de comprobarlo.

### `IEntityRules<TEntity, TCreateDto, TUpdateDto>`

Las reglas de una entidad, fuera del CRUD:

```csharp
string EntityName => typeof(TEntity).Name;
Task EnsureCanCreateAsync(TCreateDto dto, CancellationToken ct = default) => Task.CompletedTask;
Task EnsureCanUpdateAsync(int id, TUpdateDto dto, TEntity existing, CancellationToken ct = default) => Task.CompletedTask;
Task EnsureCanDeleteAsync(TEntity existing, CancellationToken ct = default) => Task.CompletedTask;
```

- Son **default interface members**: una entidad implementa solo lo que necesita
  y una entidad sin reglas no escribe nada (`NoEntityRules<,,>`).
- Un `Ensure*` **lanza `AppException`** cuando la regla se incumple. Nunca
  devuelve `bool` y nunca conoce HTTP.
- `EnsureCanUpdateAsync` corre **antes** de mapear el DTO sobre la entidad, así
  que todavía ve el estado previo.
- En un PATCH los campos pueden no venir: **toda regla empieza comprobando si el
  campo llegó** (`if (string.IsNullOrWhiteSpace(dto.Name)) return;`).

### Cómo queda un servicio de entidad

```csharp
public interface ICategoryService
  : ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> { }

public class CategoryService : ICategoryService
{
  private readonly ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> _crud;

  public CategoryService(ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> crud)
      => _crud = crud;

  public Task<IEnumerable<CategoryDto>> GetAllAsync(CancellationToken ct = default) => _crud.GetAllAsync(ct);
  public Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default)     => _crud.GetByIdAsync(id, ct);
  public Task<int> CreateAsync(CreateCategoryDto dto, CancellationToken ct = default) => _crud.CreateAsync(dto, ct);
  public Task UpdateAsync(int id, UpdateCategoryDto dto, CancellationToken ct = default) => _crud.UpdateAsync(id, dto, ct);
  public Task DeleteAsync(int id, CancellationToken ct = default)                   => _crud.DeleteAsync(id, ct);
}
```

Y toda la lógica de Category vive aquí:

```csharp
public sealed class CategoryRules(ICategoryRepository repository)
  : IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>
{
  public string EntityName => "Category";

  public async Task EnsureCanCreateAsync(CreateCategoryDto dto, CancellationToken ct = default)
  {
    if (await repository.NameExistsAsync(dto.Name, ct: ct))
      throw new ConflictAppException($"Category '{dto.Name}' already exists.");
  }
  // EnsureCanUpdateAsync: mismo chequeo con excludeId
  // EnsureCanDeleteAsync: 409 si la categoría todavía tiene productos
}
```

### Sustituir una operación concreta

Como el CRUD es un colaborador y no una clase base, un servicio puede **no
delegar** una operación. `ProductService` lo hace con las lecturas, porque el
CRUD genérico no sabe cargar la navegación `Category` y `ProductDto.CategoryName`
saldría siempre nulo:

```csharp
// escrituras: delegadas
public Task<int> CreateAsync(CreateProductDto dto, CancellationToken ct = default) => _crud.CreateAsync(dto, ct);

// lecturas: propias, con Include
public async Task<IEnumerable<ProductDto>> GetAllAsync(CancellationToken ct = default)
    => _mapper.Map<IEnumerable<ProductDto>>(await _repository.GetAllWithCategoryAsync(ct));
```

## Registro en DI

```csharp
// reglas: el genérico abierto es el "sin reglas" por defecto…
services.AddScoped(typeof(IEntityRules<,,>), typeof(NoEntityRules<,,>));
// …y el registro CERRADO por entidad gana (el contenedor prefiere la coincidencia exacta)
services.AddScoped<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>, CategoryRules>();

// CRUD: cerrado, porque ICrudService no lleva TEntity
services.AddScoped<
    ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>,
    CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>>();

// servicio de entidad
services.AddScoped<ICategoryService, CategoryService>();
```

Todo en `Shared/DependencyInjection/ServiceCollectionExtensions.cs`,
lifetime **Scoped**.

## Reglas de la capa

1. **DTO in, DTO out.** Ni una entidad cruza hacia el controller.
2. **Las reglas van en `IEntityRules`,** no repitiendo el CRUD dentro del servicio.
3. **Excepciones de dominio, siempre `AppException`.** Nunca `KeyNotFoundException`
   ni `InvalidOperationException` (ver `04-error-handling.md`).
4. **Validar FKs es responsabilidad de las reglas.** `ProductRules` comprueba que
   `dto.CategoryId` existe y lanza `BadOperationAppException`; si se deja pasar,
   EF revienta con un error de FK y sale un 500 feo.
5. **Un servicio puede depender de otro servicio,** pero el repositorio de otra
   entidad solo se usa para *consultar* (`ProductRules` lee `ICategoryRepository`
   para validar la FK); escribir en la tabla de otra entidad salta la capa.
6. **Lifetime Scoped** en el registro de DI.
7. **`CancellationToken` en toda operación async**, con `= default`, y se propaga
   hasta EF. El handler global lo traduce a 499.
8. **`IGenericService<T>` / `GenericService<T>` están retirados** (comentados como
   registro de aprendizaje). Operaban sobre entidades, no sobre DTOs.

## Sobre el DTO de update

Cada entidad tiene su `UpdateXDto` con **todos los campos nullable** — incluidos
los de tipo valor (`decimal? Price`, `int? Stock`, `int? CategoryId`). No es
cosmético: un `int CategoryId` no-nullable llega como `0` cuando el cliente no lo
envía, y el PATCH machaca la FK con un cero que revienta la restricción.

El mapeo del PATCH usa `MapFrom((s, d) => s.X ?? d.X)` campo a campo, **no**
`ForAllMembers(o => o.Condition(...))`: la `Condition` recibe el valor ya
convertido al tipo del destino, así que un `int?` nulo le llega como `0` y no lo
salta. Ver `05-convenciones.md` → Mapping.
