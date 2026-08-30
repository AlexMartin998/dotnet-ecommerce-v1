# 05 — Cache de catálogo (Redis)  ✅

Contrato: [`features/05_cache-de-catalogo.feature`](../features/05_cache-de-catalogo.feature) · Commit `34a3b44`

## Hecho
- [x] `ICacheService` + `RedisCacheService` (JSON sobre `IDistributedCache`)
- [x] `NoCacheService` (Null Object) si `Redis:Configuration` está vacío
- [x] `CacheKeys` centralizado
- [x] **`CachedCategoryService`**: decorador sobre `ICategoryService`
- [x] Registro decorado a mano en `ApplicationServiceExtensions` (sin Scrutor)

## Decisiones
- **Redis en vez de `[ResponseCache]`** (lo del curso): aquel vive en la memoria de un
  proceso, **no cachea nada** si el request lleva `Authorization`, y **no se puede invalidar**.
- **Decorador**: `CategoryService` no sabe que existe cache; se testea sin Redis y quitarla
  es borrar una línea de DI. Es `@Cacheable`/`@CacheEvict` de Spring, pero explícito.
- **Falla en abierto**: Redis caído → se sirve de la base.
- **Solo lo público se cachea**: una cache compartida con datos por usuario es una fuga.
- Se invalida **después** de que la escritura haya ido bien.

## Abierto
- Los **listados paginados no se cachean**: exigiría invalidar por prefijo o versionar la
  clave de colección. El owner propuso el enfoque de DRF (hash de la query + `delete_pattern`);
  se analizó y **`delete_pattern` no escala** (`KEYS`/`SCAN` es O(N) del keyspace) — la
  alternativa es versionar la clave, que invalida en O(1).
- `ProductService` no está decorado (solo Category).
- Sin métrica de hit-rate más allá de los logs `Cache HIT`/`MISS`.
