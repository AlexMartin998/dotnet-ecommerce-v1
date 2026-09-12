# 23 — Salir de AutoMapper (`Riok.Mapperly`)

> Cambia **quién** traduce DTO ↔ entidad. No cambia ni un contrato HTTP, ni el modelo, ni
> una migración.
>
> Nace de una revisión de licencias del stack (2026-09-12), no de un bug.

---

## 0. Por qué, y por qué NO es solo la licencia

**El dato que teníamos era falso.** El repo afirmaba en cuatro sitios que AutoMapper 15
«exige licencia comercial en producción». Verificado contra la fuente: la 15.0.1+ es dual
**RPL-1.5 + comercial**, y hay **licencia Community gratuita** por debajo de 5 M USD de
ingresos brutos. O sea que **no había que pagar**; había que registrarse. Y lo que la nota
tapaba era el riesgo de verdad: sin registrar nada, la que aplica es **RPL-1.5, copyleft
recíproco**.

Con eso claro, quedaban tres salidas: registrar la Community, fijar la 14.0.0 (última MIT),
o cambiar de mapeador. Se elige la tercera **por una razón técnica**, no por la licencia:

| | AutoMapper | Mapperly |
|---|---|---|
| Cuándo mapea | expresiones construidas **en runtime** | C# generado **al compilar** |
| Un miembro sin alimentar | excepción en la primera petición | **RMG020 = error de build** (`-warnaserror`) |
| Reflexión | sí | **ninguna** |
| Inspeccionable | no | **se lee el `.g.cs`** |
| Licencia | dual RPL-1.5 / comercial | **Apache 2.0**, sin umbral ni clave |

El repo ya pagó un **500 en producción** por un mapeo que solo falla en runtime
(`Condition` recibía un `int?` nulo como 0 y `CategoryId = 0` rompía la FK). Un mapeador
que convierte eso en un error de compilación es la lección aplicada, no una preferencia.

---

## 1. La pieza que faltaba: `IEntityMapper<,,,>`

`CrudService` es genérico y llamaba a `IMapper.Map<TDto>(...)`. Un generador de código no
puede resolver eso: la traducción tiene que entrar **por el constructor**, como ya entran
las reglas.

```csharp
// Shared/Crud/IEntityMapper.cs — gemelo de IEntityRules<,,>
public interface IEntityMapper<TEntity, TDto, TCreateDto, TUpdateDto>
{
  TDto ToDto(TEntity entity);
  TEntity ToEntity(TCreateDto dto);
  void Apply(TUpdateDto dto, TEntity entity);
}
```

- [x] Tres métodos y ni uno más: es exactamente lo que `CrudService` usaba del mapeador.
- [x] Sin genérico abierto por defecto (no hay un `NoEntityMapper`): una entidad sin
      mapeador registrado **tiene** que romper el arranque. `ValidateOnBuild` lo hace.
- [x] Registro **cerrado por entidad** en `CatalogExtensions`, como las reglas. Singleton:
      los mapeadores generados no tienen estado ni dependencias.

---

## 2. Los mapeadores del catálogo

- [x] `Features/Catalog/Mapping/CategoryMapper.cs` y `ProductMapper.cs`, `[Mapper] partial`,
      implementando la interfaz. Sustituyen a `CategoryProfile`/`ProductProfile`.
- [x] **Lo generado**: `ToDto` y un `Build` privado para la creación.
- [x] **Lo escrito a mano**: `Apply` (el PATCH parcial) y el `Trim` al escribir.

### Por qué el PATCH sigue siendo campo a campo

Mapperly tiene `AllowNullPropertyAssignment = false`, documentado justo para esto. **No se
usa.** El PATCH es donde este repo ya se quemó una vez, y `entity.X = dto.X ?? entity.X`
son siete líneas que no hay que ir a verificar en la documentación de nadie:

```csharp
entity.CategoryId = dto.CategoryId ?? entity.CategoryId;   // el que reventaba la FK
```

Es menos código que la versión con AutoMapper y no depende de la semántica interna de
ninguna librería. El generador se queda con lo que hace bien: las proyecciones.

### Lo que el generador resolvió solo, y se comprobó leyéndolo

```csharp
// obj/.../ProductMapper.g.cs
target.CategoryName = entity.Category?.Name;      // null-safe: un GET sin .Include no revienta
target.RowVersion   = ToBase64(entity.RowVersion); // toma el método propio por FIRMA
```

- [x] `[MapProperty("Category.Name", ...)]` para aplanar la navegación.
- [x] `private static string? ToBase64(byte[]?)` en la clase: Mapperly lo usa para esa
      propiedad sin más configuración.
- [x] `[MapperIgnoreTarget]` para `Id`, `RowVersion`, `Category` y la auditoría al escribir.
- [x] `[MapperIgnoreSource]` para `CreatedAt`/`UpdatedAt` en `CategoryDto`, que no los
      expone. **Esto lo pidió el compilador**: dos RMG020 en el primer build.

---

## 3. Lo que desaparece

- [x] `Shared/Mapping/MappingExtensions.cs` (`AddObjectMapping`): ya no hay ensamblados que
      escanear. **Con él se va una de las dos excepciones documentadas** a «`Shared/` no
      nombra tipos de `Features/`»: el composition root ya no necesita
      `typeof(CategoryProfile).Assembly`.
- [x] `Features/Catalog/Mapping/CategoryProfile.cs` y `ProductProfile.cs`.
- [x] El paquete `AutoMapper`.
- [x] El test `Configuration_IsValid`: su equivalente es el build.

⚠️ **Lo que NO se toca**: `Shared/Mapping/MappingProfile.cs` y los bloques comentados de
`CategoryService`/`CategoryController`. Son registro de aprendizaje (`rules.md` §1.2) y
mencionan `IMapper` dentro de comentarios.

---

## 4. Verificación

- [x] `dotnet build -warnaserror` → **0 warnings** (los dos RMG020 arreglados, no
      silenciados).
- [x] `dotnet test` → **322/322** (318 + 5 nuevos − 1 que el compilador hace ahora).
      Nuevos: Trim al crear y al actualizar en producto y categoría, y el rowversion en
      base64 con su caso nulo.
- [x] Leído el `.g.cs` generado: cero reflexión, null-safety en la navegación.
- [x] **Ejecutando**, contra SQL Server y Redis reales:

| Prueba | Resultado |
|---|---|
| POST con `"  Producto MIG  "` | `"Producto MIG"` — Trim aplicado |
| GET del producto | `categoryName: "Bebidas MIG"`, `rowVersion: "AAAAAAACgKI="` |
| **PATCH solo con `name`** | `categoryId` 6092, `price` 99.9, `stock` 10, `sku` intactos |
| PATCH con `stock: 0` | stock 0 — un 0 explícito **sí** se aplica |
| PATCH con `description: ""` | queda `""`; `imageUrl`, que no se mandó, sigue null |
| PATCH de categoría solo con `description` | el `name` aguanta (y pasa por el decorador de cache) |
| `GET /product/paged` | 2 items de 162, con `categoryName` resuelto |
| Arranque en `Production` sin Redis ni broker | `/health` 200, `/health/ready` **Healthy**, 0 errores |

⚠️ **Hallazgo lateral**: el `Trim` del SKU es **inalcanzable por HTTP**. La DataAnnotation
del DTO rechaza espacios antes de que el mapeador vea el valor (400, `"SKU can only contain
letters, digits and hyphens"`). Se deja: es defensa en profundidad para cualquier otro
llamador, y el test unitario lo cubre.

---

## 5. Lo que queda abierto

- **Solo `Catalog` tiene mapeadores.** `Accounts` proyecta a mano (`IdentityMapping`, porque
  los roles son una consulta aparte), y `Ordering`/`Payments` no usan el CRUD genérico. Una
  entidad nueva en el CRUD necesita su `IEntityMapper`, y si se olvida **no arranca**.
- **La decisión del owner sobre AutoMapper queda cerrada** por la vía de no usarlo. Si
  algún día se quiere volver, la última MIT es la **14.0.0**.
