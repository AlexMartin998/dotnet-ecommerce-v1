# 26 — Catálogo de demostración traído de Teslo Shop

> Pedido del owner (2026-09-13): sustituir el catálogo sembrado por el seed de la tienda
> Next.js (`006_teslo-shop/src/api/db/seed/seed-data.ts` + `public/products`), y **limpiar la
> base local de desarrollo menos los usuarios**. Autorización explícita para esa limpieza,
> **solo esta vez**, porque la base es local y no hay producción (`rules.md` §11.1).
> Contrato: `features/26_catalogo-de-demo-teslo.feature`.

---

## 0. El modelado

| Teslo (`SeedProduct`) | Aquí | Por qué |
|---|---|---|
| `type` (`shirts`/`pants`/`hoodies`/`hats`) | **`Category`** `Shirts`/`Pants`/`Hoodies`/`Hats` | Es lo que hacía el propio Prisma de Teslo. `Pants` queda vacía, igual que allí |
| `gender` (`men`/`women`/`kid`/`unisex`) | **una etiqueta más** en `Tags` | `Product` no tiene género y no se le añade una columna para una demo: filtrar por etiqueta es para lo que existen |
| `title` | `Name`, con `’` → `'` | Buscar «men's» no encontraría el apóstrofo tipográfico |
| `slug` (`mens_chill_crew`) | `Slug` con `_` → `-` | Derivarlo del nombre daría `men-s-chill-…`. Cumple el regex del DTO |
| código de la imagen (`1740176-00-A`) | **`SKU`** | Es la referencia real de la prenda, única en los 52 y válida para el regex del DTO |
| `inStock`, `price`, `sizes`, `description` | `Stock`, `Price`, `Sizes`, `Description` | Directo. Descripción máx. 463 < 500 |
| `images[]` | `ProductImage` con `Position` 0 y 1 | Subidas por `IFileStorage`, no copiadas a mano |
| `users` | **no se traen** | El owner pide conservar los usuarios que hay |

## 1. Dónde vive

- [x] `Data/Seed/storefront-catalog.json` — **recurso embebido**: el catálogo se siembra
      también en un despliegue que no lleve las imágenes.
- [x] `Data/Seed/images/` — las 104 imágenes (21 `.jpg` y 83 `.webp`, 7 MB). Se copian a la salida de build y
      **no a la de publish** (`CopyToPublishDirectory=Never`): la imagen de producción no
      carga con fotos de demo.
- [x] `DataSeeder.SeedCatalogAsync` lee el JSON, sube cada imagen por
      `IFileStorage.SaveImageAsync` y guarda **todo en un solo `SaveChangesAsync`**.
      - Sin la carpeta de imágenes: warning y productos sin imagen.
      - ⚠️ Las imágenes se escriben antes del commit: si el guardado falla quedan ficheros
        huérfanos en `wwwroot/ProductsImages`. Se acepta en un seeder de desarrollo; al
        revés (commit primero) un fallo dejaría productos sin imagen y el seeder ya no
        volvería a intentarlo porque las categorías existen.
- [x] 🔴 **83 de las 104 imágenes de Teslo eran WebP con extensión `.jpg`.** El navegador las
      pinta igual, pero `LocalFileStorage` compara firma y extensión y el primer arranque murió
      con `The file content does not match a supported image format`. Se renombraron a
      `.webp`, y el seeder manda el `Content-Type` según la extensión. Es la prueba de que
      pasar por `IFileStorage` valía la pena: copiadas a mano habrían entrado sin mirar.
- [x] `StorefrontCatalogSeedTests`: el FICHERO pasa la validación de `CreateProductDto`, SKU y
      slug únicos, género como etiqueta, y cada imagen existe, cabe en 2 MB y su firma casa
      con su extensión. Un dato malo tumbaría el único `SaveChanges` en el arranque.
- [x] Tests: `Seed:IncludeDemoData=false` en `ApiFactory`. Ningún test depende del
      catálogo sembrado, y con imágenes cada corrida dejaría 104 ficheros en `wwwroot`.

## 2. Limpieza de la base local (autorizada, una sola vez)

Se **conservan**: `AspNet*` (usuarios, roles y sus relaciones), `RefreshTokens` (son sesiones
de esos usuarios) y `__EFMigrationsHistory`.

Se **vacían**, en una transacción y en orden de dependencias: `OrderItems`, `Payments`,
`ProcessedWebhookEvents`, `Orders`, `ProductImages`, `Products`, `Categories`,
`OutboxMessages`, `ProcessedMessages`, `ExecutedCommands`.

- [x] Identidades a cero (`DBCC CHECKIDENT … RESEED`) y secuencias `OrderNumbers` y
      `PaymentReferences` a 1: la próxima orden vuelve a ser `ORD-2026-000001`.
- [x] Ficheros que apuntaban esas filas: `wwwroot/ProductsImages/*` (2, menos `.gitkeep`) y
      `App_Data/documents` (93 comprobantes).
- [x] Redis: solo las claves con prefijo `apiecommerce:` (cache de categorías e idempotencia).
      Había 0: ya habían expirado.
      ⚠️ El Redis es compartido: **nunca** `FLUSHDB`.
- [x] RabbitMQ: las colas de `apiecommerce.*` ya estaban vacías, no se toca.
- [x] `dotnet publish` del proyecto: sin `Data/Seed/images` en la salida.

**Qué deja de ser posible después**: consultar las órdenes, pagos y comprobantes de prueba
anteriores, y que un cliente repita una `Idempotency-Key` vieja sin volver a ejecutar.

## 3. Verificación

- [x] `dotnet build -warnaserror` limpio y suite **390/390** (+5).
- [x] Limpieza aplicada (83.310 filas; usuarios 20, roles y sesiones intactos) y API arrancada:
      4 categorías, 52 productos (ids 1–52), 104 imágenes. `Pants` → `200 []`.
- [x] `GET /product/slug/mens-chill-crew-neck-sweatshirt` 200 con sus 2 imágenes, y la URL
      de la imagen servida con 200 `image/webp`. `/health/ready` Healthy.
- [x] Segundo arranque: `Catalog already seeded, skipping` y los mismos recuentos.
