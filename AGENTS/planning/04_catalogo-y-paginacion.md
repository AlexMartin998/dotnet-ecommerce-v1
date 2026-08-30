# 04 — Catálogo y paginación  ✅

Contrato: [`features/04_catalogo-y-paginacion.feature`](../features/04_catalogo-y-paginacion.feature) · Commit `34a3b44`

## Hecho
- [x] `Shared/Paging/PagedResult<T>` (record) con `TotalItems` + calculados
- [x] `Models/Dtos/PageQuery` con `[Range(1, 100)]` en `PageSize`
- [x] `BaseRepository.GetPagedAsync` reusando `ApplyDefaultOrder`
- [x] `ProductRepository.GetPagedWithCategoryAsync` con `Include` y desempate por `Id`
- [x] Endpoints `/paged` en Category y Product

## Decisiones
- **`TotalItems` se expone**: es el dato del "mostrando 1-10 de 137". El curso lo
  calculaba y lo tiraba.
- **Página fuera de rango → 200 con `[]`**, no 404: la colección existe.
- El tope de `pageSize` no es decorativo: sin él, `?pageSize=1000000` en un endpoint
  anónimo es un DoS de una sola petición.
- El `COUNT` va sobre la **misma consulta base** que la página (si no, en cuanto haya un
  filtro `TotalItems` miente).

## Abierto
- `GET /api/v1/category` y `GET /api/v1/product` **siguen sin paginar**. Deprecarlos por
  versión (una v2 sin ellos), no borrarlos: sería breaking.
- Paginación por offset; con tablas grandes degrada. Keyset sería lo correcto.
- Sin filtros ni ordenación configurables.
