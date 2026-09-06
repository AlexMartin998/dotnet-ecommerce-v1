# 01 — Capas y contratos

Cada capa tiene un contrato de entrada, uno de salida y una lista de cosas que
tiene **prohibido** hacer. Si una regla se rompe, la responsabilidad se filtró a
la capa equivocada.

---

## Controller

**Entra:** DTO de request (`CreateXDto`, `UpdateXDto`), parámetros de ruta/query.
**Sale:** `ActionResult<TDto>` / `IActionResult` con DTOs.
**Depende de:** `IXService`. Nada más.

Responsabilidades:

- Definir ruta, verbo, nombre de ruta y `[ProducesResponseType]`.
- Validar `ModelState` y devolver `ValidationProblem(ModelState)`.
- Traducir el resultado del servicio al `IActionResult` del **camino feliz**
  (`Ok`, `CreatedAtRoute`, `NoContent`).

Prohibido:

- ❌ `try/catch` de excepciones de negocio → lo maneja el handler global.
- ❌ Inyectar `IMapper`, `IXRepository` o `AppDbContext`.
- ❌ Consultas LINQ, reglas de unicidad, cálculos de stock, etc.
- ❌ Devolver entidades de `Features/<Slice>/Models/` en el body.

```csharp
[HttpPost(Name = "CreateCategory")]
[ProducesResponseType(StatusCodes.Status201Created)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status409Conflict)]
public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryDto dto)
{
    if (!ModelState.IsValid)
        return ValidationProblem(ModelState);

    // si el nombre ya existe, CategoryService lanza ConflictAppException
    // y el handler global responde 409. Aquí no hay catch.
    var newId = await _service.CreateAsync(dto);
    return CreatedAtRoute("GetCategory", new { id = newId }, null);
}
```

---

## Service

**Entra:** DTOs.
**Sale:** DTOs (o `int`/`void` para create/update/delete).
**Depende de:** `ICrudService<...>` (el CRUD compuesto), `IXRepository` para sus
consultas propias, `IMapper`, y otros `IXService` si hace falta.
Las reglas de negocio no viven en el servicio sino en su `IEntityRules`
(`CategoryRules`, `ProductRules`) — ver `03-service.md`.

Responsabilidades:

- Reglas de negocio y validaciones que necesitan tocar la base
  (unicidad, existencia de la FK, stock suficiente).
- Mapear entidad ↔ DTO con AutoMapper.
- **Lanzar `AppException`** cuando la regla se incumple. El servicio es el único
  lugar donde nacen las excepciones de dominio.
- Orquestar varios repositorios en una operación.

Prohibido:

- ❌ Devolver entidades hacia el controller.
- ❌ Conocer `HttpContext`, `IActionResult` o `StatusCodes`. El servicio expresa
  intención de dominio (`NotFoundAppException`), no HTTP; la traducción a 404
  es del handler.
- ❌ Usar `AppDbContext` directamente. Si necesitas una consulta que el
  repositorio no ofrece, **agrégala al repositorio**, no al servicio.
- ❌ Lanzar excepciones BCL (`KeyNotFoundException`, `InvalidOperationException`)
  como señal de negocio.
- ❌ Reescribir el CRUD a mano. Si te encuentras copiando los cinco métodos de
  otra entidad, lo que falta es delegar en `ICrudService`.

---

## Repository

**Entra:** entidades, ids, criterios primitivos.
**Sale:** entidades, `bool`, `IEnumerable<T>` / `ICollection<T>`.
**Depende de:** `AppDbContext`.

Responsabilidades:

- Traducir intención de persistencia a LINQ/EF Core.
- `SaveChangesAsync()` en cada operación de escritura (hoy no hay unit of work;
  ver `02-repository.md`).
- `.AsNoTracking()` en todas las lecturas que no se van a modificar.

Prohibido:

- ❌ Conocer DTOs o `IMapper`.
- ❌ Lanzar excepciones de negocio. Un id inexistente devuelve `null`, no un throw.
- ❌ Aplicar reglas de negocio ("no se puede borrar si tiene productos" es del
  servicio; el repositorio solo ofrece `HasProductsAsync(categoryId)`).

---

## Models / Dtos

- `Models/X.cs` es la **entidad EF Core**: clave, índices, FKs, propiedades de
  navegación, `[Column(TypeName = ...)]` para precisión decimal.
- `Features/<Slice>/Dtos/` es el **contrato público de la API**. Cambiar un DTO es un
  cambio breaking; cambiar una entidad es una migración. Son ejes distintos y
  por eso no se comparten tipos.
- Tres DTOs por entidad, con propósitos distintos:

| DTO | Uso | Validación |
| --- | --- | --- |
| `XDto` | respuesta (GET, y cuerpo de listados) | ninguna |
| `CreateXDto` | body de POST | DataAnnotations **completas y obligatorias** |
| `UpdateXDto` | body de PATCH | DataAnnotations, **todo nullable, tipos valor incluidos** |

En `UpdateXDto` los tipos valor van como `decimal?` / `int?`, no como `decimal` /
`int`. Un `int CategoryId` no-nullable llega como `0` cuando el cliente no lo
envía y el PATCH machaca la FK con un cero que revienta la restricción.

`XDto` nunca expone propiedades de navegación completas — expone `CategoryId`, y
si el caso de uso pide el nombre de la categoría, se agrega `CategoryName` como
propiedad plana mapeada en el `Profile`.

---

## Mapping

Un `Profile` por entidad en `Features/<Slice>/Mapping/XProfile.cs`. Registrados por escaneo de
assembly en `Program.cs`, así que **crear el archivo basta**, no hay que
registrarlo a mano.

- `CreateXDto → X` es la dirección que importa en escritura.
- `X → XDto` es la que importa en lectura.
- Usar `.ReverseMap()` solo cuando las dos direcciones se usan de verdad;
  el `ReverseMap()` indiscriminado esconde mapeos que nadie ejercita y falla
  tarde.
- El update se hace **sobre la entidad rastreada**: `_mapper.Map(dto, existing)`,
  nunca `_mapper.Map<X>(dto)` + asignar `Id` (eso pierde `CreatedAt` y demás
  campos no incluidos en el DTO).
- El PATCH parcial se expresa campo a campo con `MapFrom((s, d) => s.X ?? d.X)`,
  **no** con `ForAllMembers(o => o.Condition(...))`: la `Condition` recibe el
  valor ya convertido al tipo del destino, así que un `int?` nulo le llega como
  `0` y no lo salta.
- `CreatedAt` / `UpdatedAt` se **ignoran** en todos los mapeos de escritura: los
  estampa `AppDbContext`.

---

## Shared

- `Shared/Db/` — infraestructura de persistencia transversal
  (`TransactionalAttribute`, futuros interceptores de auditoría).
- `Shared/Http/` — infraestructura HTTP transversal (`GlobalExceptionHandler`,
  formato de error). **Es la única carpeta, junto a `Controllers/`, que puede
  conocer códigos HTTP.**
- `Shared/DependencyInjection/` — `ServiceCollectionExtensions`, el **composition
  root**: no registra nada, compone en tres bloques (`AddSharedInfrastructure`,
  `AddFeatures`, `AddWebApi`) los `Add…` que cada feature declara en su
  propia carpeta. Ver `05-convenciones.md` → Inyección de dependencias.


## Idempotencia: dónde va cada mitad

Es el ejemplo más claro de que «una preocupación» pueden ser **dos**, y de que partirla
por la costura correcta importa más que elegir una capa.

| | Dónde | Por qué |
|---|---|---|
| Leer `Idempotency-Key`, validar su forma, elegir 400/409 | **Adaptador** (`[Idempotent]`, controller) | Es protocolo. El servicio no conoce `StatusCodes`, y un job no manda cabeceras |
| «Este intento no se ejecuta dos veces» | **Servicio**, dentro de su transacción (`ICommandLog`) | Es una invariante de negocio, indistinguible de «descontar stock y emitir el evento son atómicos» |

La prueba de que el corte está bien hecho: la operación recibe una `CommandIntent`
**obligatoria**, así que un llamador nuevo —un job, otro endpoint— no puede perder la
garantía por descuido. Es exactamente lo que se ganó al mover la transacción del
`[Transactional]` del controller a `ITransactionRunner`.

⚠️ Y al revés: lo que **no** debe bajar es la respuesta HTTP. Memorizar `StatusCode` y
cabeceras en el servicio lo ataría al protocolo — y de hecho, memorizar el **DTO** en vez
de la respuesta es lo que hizo que el replay volviera a ser idéntico byte a byte, porque
vuelve a pasar por el mismo formateador.
