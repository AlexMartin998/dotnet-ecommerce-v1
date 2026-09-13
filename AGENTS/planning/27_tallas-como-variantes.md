# 27 — Tallas como variantes, con stock por talla

> Pedido del front (`ecom_angular`, `02_front/.../AGENTS/context/api-contract-02-product-sizes.md`),
> en nombre del owner: «con una tabla nueva, la mejor decisión respetando la arquitectura».
> Las tres operaciones de §11.1 las **autorizó el owner en esta sesión** (2026-09-13):
> resembrar el catálogo, borrar `Products.Stock`/`Products.Sizes` y FK `OrderItems.VariantId`.
> Contrato: `features/27_tallas-como-variantes.feature`.

---

## 0. La decisión

**`ProductVariant` en `Catalog`**: producto, talla, SKU único, stock, posición, activa.
**Todo producto tiene al menos una variante**; uno sin tallas tiene **una sola, sin talla**, con
el SKU del producto.

| Alternativa | Por qué no |
|---|---|
| `size` en la línea + stock por talla aparte | La clave de línea pasa a ser compuesta (`sku+size`), `QuoteCartDto`/`PlaceOrderDto` cambian de forma y el UPDATE condicional necesita dos columnas en el `WHERE`. Una variante con SKU es lo que ya es un SKU en logística |
| Variantes solo para productos con talla, stock en `Product` para el resto | Dos caminos para descontar stock, dos para devolver, dos para las stats. Cada regla de §7.1 escrita dos veces |
| Mantener `Product.Stock` como suma desnormalizada | Dos UPDATE por compra (producto y variante) y un orden de locks más que respetar. Se calcula al leer |

- **Encaja en §5.2**: una talla es vocabulario del catálogo, no un slice. `Ordering` sigue sin
  conocerla: habla con `ICatalogGateway`, que ahora responde por SKU de variante.
- **§7.1 intacto**: el contador con contención sigue siendo un UPDATE condicional
  (`WHERE Id AND IsActive AND Stock >= @q`), solo cambia de tabla. Editar una variante desde
  admin es concurrencia optimista (`RowVersion` + `If-Match` → 412), igual que el producto.
- **Una talla no se borra, se desactiva** (R6): la FK desde `OrderItems` lo hace obligatorio.
- **El SKU y la talla de una variante no cambian** tras crearla: el SKU vive en carritos de
  `localStorage` y en órdenes. Para otra talla, otra variante.
- **La línea copia `Size`** además de `Sku`/`Name`/`UnitPrice`: el comprobante sigue diciendo
  la talla aunque luego se desactive (R4, R8).

## 1. Modelo y migración

- [x] `ProductVariant : IAuditable` — `Size` (`MaxLength(20)`, nullable), `SKU` (`MaxLength(50)`,
      índice único **filtrado por `DeletedAt`**), `Stock`, `Position`, `IsActive`, `DeletedAt`,
      `[Timestamp] RowVersion`.
- [x] 🔴 **Lo que destapó la suite**: con la variante sin talla, retirar un producto **ya no
      liberaba su SKU** (`AWithdrawnProductReleasesItsSkuAndItsSlug` en rojo). El índice de
      `ProductVariants.SKU` no puede filtrar por `Products.DeletedAt` (otra tabla), así que la
      variante lleva **una copia** y `SoftDeleteAsync` estampa las dos en una transacción.
- [x] Índice único `(ProductId, Size)` (EF lo filtra por `Size IS NOT NULL`). Cascade desde
      `Product` como las imágenes; filtro global por `Product.DeletedAt`.
- [x] `Product`: fuera `Stock` y `Sizes`; dentro `Variants`.
- [x] `OrderItem`: `VariantId` (nullable, FK Restrict) y `Size` (nullable).
- [x] Migración **sin relleno**: el catálogo se resiembra (0 órdenes en la base local). ⚠️ En
      una base con datos reales perdería el stock: lo dice un comentario en la migración.

## 2. Catálogo

- [x] `IProductVariantRepository`: por SKU, decremento/incremento condicional, SKU libre.
- [x] `ProductDto.Variants` (activas, por posición); `Stock` y `Sizes` **derivados** de ellas.
- [x] `CreateProductDto`: `Stock` (`int?`) **o** `Variants` (`[{ size, sku?, stock }]`), no los
      dos (400). Sin `Variants` nace la variante sin talla. `Sizes` sale del DTO.
- [x] `UpdateProductDto`: fuera `Stock` y `Sizes` (el stock se edita en su variante).
- [x] `ProductVariantController` — `api/v1/product/{productId}/variants`, solo admin:
      `GET`, `POST` (201), `PATCH /{variantId}` (`stock`, `position`, `isActive`, `If-Match`).
- [x] Reglas: `duplicate_size` 409, `variant_kind_mismatch` 409 (una variante sin talla activa
      no convive con tallas activas), SKU ocupado 409.
- [x] `POST /product/buy` y `CountStockAsync` sobre variantes.

## 3. Ordering

- [x] `ICatalogGateway`: `TryTakeAsync` devuelve un resultado con motivo (`NotFound`,
      `Unavailable`, `InsufficientStock`); `PeekAsync` trae `Size` e `IsActive`;
      `ReturnAsync(variantId?, sku, qty)` — por id, o por SKU en una línea antigua.
- [x] Orden: 409 `sku_not_found` / `sku_unavailable` / `insufficient_stock` con extensión `sku`.
      `AppException.Extensions` para llevarla.
- [x] Cotización: status `unavailable` nuevo; `CartLineDto.Size`, `OrderItemDto.Size`.
- [x] Recolector: devolver en orden de **SKU**, el mismo que al comprar (antes era por
      producto, distinto del de compra: un cruce cancelar/comprar podía hacer deadlock).
- [x] Comprobante: la talla junto al nombre.

## 4. Seed

- [x] JSON: `variants: [{ size, stock }]` por producto; SKU `{sku}-{size}`. Los dos gorros
      **sin tallas**; XXL de «Men's Chill Crew Neck Sweatshirt» **agotada**.
- [x] Base local: vaciar `Categories`/`Products`/`ProductImages` (+ ficheros), migrar, resembrar.

## 5. Verificación

- [x] Build `-warnaserror` limpio y suite **420/420** (+29: `ProductVariantTests` ×25,
      `VariantSkusTests` ×4; el test del seed ganó comprobaciones). Ningún test previo se borró; se adaptaron los que
      leían `Product.Stock`.
- [x] Concurrencia ejecutando: 10 órdenes **simultáneas** por la última unidad de una talla →
      1 × 201, 9 × 409, stock 0, la otra talla intacta.
- [x] Rollback: una línea que falla deshace la que ya se había apartado (la de SKU menor).
- [x] Migración revisada antes de aplicarla: create + 2 columnas nullable + FK + drop de
      `Stock`/`Sizes`, sin relleno. `Down` corregido: `Sizes` vuelve con `"[]"`, no `""`.
- [x] Base local: catálogo vaciado (0 órdenes), migración aplicada al arrancar y resembrado:
      `52 products, 194 variants and 104 images`. XXL de «Chill Crew» con stock 0 y
      `available: false`, gorros con una variante `size: null`, `/health/ready` Healthy.
- [x] OpenAPI regenerado (`/swagger/v1/swagger.json`): 47 rutas, `ProductVariantDto`,
      `CreateProductVariantDto`, `UpdateProductVariantDto`.

## 6. Revisión por agente (§9), verificando ejecutando — 5 bugs reales

| # | Bug reproducido | Arreglo | Test |
|---|---|---|---|
| 1 | 🔴 PATCH que reactiva la variante sin talla + POST de una talla a la vez → **las dos formas activas** en 23/25 intentos. Comprobar-y-escribir sin serializar | Transacción + `sp_getapplock` por producto (`LockProductAsync`). Un applock y no un UPDATE del producto: tocar su fila cambiaría su `RowVersion` | `...NeverMixesThem` — **sin el lock, falla** (comprobado quitándolo) |
| 2 | `"variants":[null]` → 500 `NullReferenceException` | `CreateProductDto : IValidatableObject` | `ANullSizeInTheList...` |
| 3 | `sku: ""` → variante con SKU vacío, incomprable | Vacío = «no viene» y se deriva | `AnEmptySkuMeansDeriveIt` |
| 4 | Dos tallas a `int.MaxValue` → ficha, listado y `/stats` en 500 (overflow en C# y 8115 en SQL) | Tope 1.000.000 por talla, 30 tallas por producto, suma en `long` saturada | `StockHasAnUpperBound...` |
| 5 | PATCH del SKU de un producto sin tallas lo separaba de su variante (ficha incomprable), y podía tomar el SKU de la talla de otro producto | El PATCH sincroniza la variante sin talla en la misma transacción; la regla mira también `ProductVariants` | `Renaming...`, `AProductSkuCannotTake...` |

Además: el agente vio **deadlocks resueltos por reintento** al retirar un producto mientras se
compraban sus tallas (la compra hacía JOIN a `Products` por el filtro global; el retiro toma
los locks al revés). La compra ya no usa el filtro: mira la **copia** `DeletedAt` de la
variante y solo toca `ProductVariants`.

Queda sin cambiar, anotado: el choque de dos POST de la misma talla que arbitra el índice
único sale como `code: conflict` y no `duplicate_size` (el handler genérico).
