# AGENTS/progress.md — Bitácora de avance y pendientes

> Qué está hecho, qué está a medias y qué falta, con la evidencia de cómo se verificó.
> **Se actualiza en el mismo commit que el código.** El diseño objetivo vive en
> `docs/06-estado-y-roadmap.md`; esto es la foto de ejecución.

Última actualización: **2026-09-13** (tallas como variantes con stock por talla.
420/420 tests).

---

## 1. Estado por slice

| # | Slice | Estado | Artefacto | Commit |
|---|---|---|---|---|
| 01 | Auth: Identity + JWT | ✅ | [`features/01`](features/01_auth-y-registro.feature) · [`planning/01`](planning/01_auth-y-registro.md) | `34a3b44` |
| 02 | Autorización por roles | ✅ | [`features/02`](features/02_autorizacion-por-roles.feature) · [`planning/02`](planning/02_autorizacion-por-roles.md) | `34a3b44` |
| 03 | Versionado de API + Swagger | ✅ | [`features/03`](features/03_versionado-de-api.feature) · [`planning/03`](planning/03_versionado-de-api.md) | `34a3b44` |
| 04 | Catálogo y paginación | ✅ | [`features/04`](features/04_catalogo-y-paginacion.feature) · [`planning/04`](planning/04_catalogo-y-paginacion.md) | `34a3b44` |
| 05 | Cache de catálogo (Redis) | ✅ | [`features/05`](features/05_cache-de-catalogo.feature) · [`planning/05`](planning/05_cache-de-catalogo.md) | `34a3b44` |
| 06 | Imágenes de producto | ✅ | [`features/06`](features/06_imagenes-de-producto.feature) · [`planning/06`](planning/06_imagenes-de-producto.md) | `34a3b44` |
| 07 | Compra y concurrencia | ✅ | [`features/07`](features/07_compra-y-concurrencia.feature) · [`planning/07`](planning/07_compra-y-concurrencia.md) | `63269ac` |
| 08 | Idempotencia de peticiones | ✅ | [`features/08`](features/08_idempotencia.feature) · [`planning/08`](planning/08_idempotencia.md) | `63269ac` |
| 09 | Eventos de dominio (outbox + RabbitMQ) | ✅ | [`features/09`](features/09_eventos-de-dominio.feature) · [`planning/09`](planning/09_eventos-de-dominio.md) | `63269ac` + fix |
| 10 | Límites, salud y despliegue | ✅ | [`features/10`](features/10_limites-y-salud.feature) · [`planning/10`](planning/10_limites-y-salud.md) | `63269ac` |
| 11 | **Tests** | ✅ fases 1–6 (**202 tests** + CI) | [`planning/11`](planning/11_proyecto-de-tests.md) | `14c9e76` + |
| 12 | Deuda de la revisión 2026-08-30 | ✅ (§12.5 en [`planning/18`](planning/18_deuda-de-mensajeria.md)) | [`planning/12`](planning/12_deuda-revision-multiagente.md) | — |
| 13 | Refresh tokens y revocación | ✅ | [`features/13`](features/13_refresh-tokens.feature) · [`planning/13`](planning/13_refresh-tokens.md) | — |
| 14 | Administración de usuarios | ✅ | [`features/14`](features/14_admin-usuarios.feature) · [`planning/14`](planning/14_admin-usuarios.md) | — |
| 15 | Partir en proyectos | ❌ diferido | [`planning/15`](planning/15_partir-en-proyectos.md) | — |
| 16 | Idempotencia bajo carga | ✅ | [`features/16`](features/16_idempotencia-bajo-carga.feature) · [`planning/16`](planning/16_idempotencia-bajo-carga.md) | `7f120a2` |
| 17 | Idempotencia transaccional | ✅ | [`features/17`](features/17_idempotencia-transaccional.feature) · [`planning/17`](planning/17_idempotencia-transaccional.md) | `b9f62aa` |
| 18 | Deuda de mensajería | ✅ | [`features/18`](features/18_mensajeria-robusta.feature) · [`planning/18`](planning/18_deuda-de-mensajeria.md) | `a77b5df` |
| 19 | Errores bajo carga | ✅ | [`planning/19`](planning/19_errores-bajo-carga.md) | `347f901` |
| 20 | **Órdenes y comprobante en PDF** | ✅ | [`features/20`](features/20_ordenes-y-comprobante.feature) · [`planning/20`](planning/20_ordenes-y-comprobante.md) | `7df6df0` |
| 21 | Recuperar de la DLQ y recoger basura | ✅ | [`features/21`](features/21_recuperar-comprobantes-y-recoger-basura.feature) · [`planning/21`](planning/21_recuperar-comprobantes-y-recoger-basura.md) | — |
| 22 | **Pagos (Stripe) y el ciclo de vida de la orden** | ✅ ⚠️ camino feliz contra Stripe **sin verificar**: necesita `sk_test_…` y URL pública | [`features/22`](features/22_pagos.feature) · [`planning/22`](planning/22_pagos.md) | `3769e99` |
| 23 | Salir de AutoMapper (`Riok.Mapperly`) | ✅ | [`planning/23`](planning/23_mapeador-sin-licencia.md) | — |
| 25 | Identificadores públicos no enumerables | ✅ | [`features/25`](features/25_identificadores-publicos.feature) · [`planning/25`](planning/25_identificadores-publicos.md) | — |
| 24 | **Paridad con TesloShop**: carrito, fulfillment, contadores y catálogo | ✅ pasos 1–4; 5–6 abiertos | [`features/24`](features/24_carrito-y-fulfillment.feature) · [`planning/24`](planning/24_paridad-con-tesloshop.md) | — |

**Ya no queda ningún ⚠️.** El 09 se cerró el 2026-09-05 contra un RabbitMQ real, y esa
verificación destapó un bug que el build y el smoke test no veían (abajo).

---

## 2. Bitácora

### 2026-09-13 — La clave primaria deja de salir en las rutas

`planning/25`, pedido por el front Angular: con `/cuenta/pedidos/71` basta con sumar uno para
descubrir órdenes. **Órdenes y pagos** pasan a un `publicId` (UUID v7) con índice único, y sus
DTOs dejan de llevar `id`/`orderId`; **las categorías** ganan `slug` (inmutable, como el de
producto), con `GET /category/slug/{slug}` y `GET /product/category/slug/{slug}`. El catálogo
conserva su `id` en las rutas de admin: es público y no filtra nada.

🔴 **EF volvió a generar la migración mal**, igual que el día anterior: 96 órdenes con el mismo
`Guid.Empty` y un índice único encima. Reordenada a mano: columnas → relleno (`NEWID()`, copia de
`OrderPublicId`, slug colapsando espacios en T-SQL) → índices. Antes de aplicarla se miró la
base: 158 categorías, todas con letras, dígitos y espacios, 0 colisiones de slug, 0 pagos.

⚠️ `number` y `reference` siguen siendo secuenciales: ya no enumeran, pero dejan estimar el
volumen. Queda como decisión del owner.

**Verificado ejecutando** contra la base de desarrollo migrada:

| Prueba | Resultado |
|---|---|
| Los 158 slugs rellenados en SQL | iguales a `Slugs.From` en los 158 |
| `GET /category/slug/…` y `/product/category/slug/…` anónimos | 200, con `categorySlug` |
| `POST /order` | 201, `Location` con el UUID v7, sin `id` en el cuerpo |
| `GET /order/{publicId}` · `GET /order/{su PK}` | 200 · **404** |
| receipt · `PATCH status` · `POST /payment {orderPublicId}` por `publicId` | 409 `receipt_not_ready` · 409 `invalid_transition` · 503 `no_payment_provider` (sin Stripe) |
| `POST /payment` sin `orderPublicId` | 400 de validación |

Suite **385/385** (+8), build limpio con `-warnaserror`. De paso: `GetProductsForCategoryAsync`
ordenaba sin desempate por PK (CLAUDE.md §9).


### 2026-09-13 — Tallas como variantes, con stock por talla

`planning/27`, pedido por el front. `ProductVariant` en `Catalog` (talla, SKU único, stock,
posición, activa); **todo producto tiene al menos una**, y uno sin tallas tiene la suya sin
talla con el SKU del producto. La clave de línea sigue siendo `sku`, así que cotización y
orden no cambian de forma. `OrderItem` copia `Size` y guarda `VariantId` (FK). Admin en
`/product/{id}/variants`. Errores de línea con `code` y extensión `sku`
(`sku_not_found` · `sku_unavailable` · `insufficient_stock`), vía `AppException.Extensions`.

Autorizado por el owner (§11.1): borrar `Products.Stock`/`Sizes`, FK en `VariantId` y
**resembrar** en vez de rellenar (0 órdenes). 🔴 La suite destapó que la variante sin talla
**volvía a bloquear el SKU de un producto retirado**: `ProductVariant.DeletedAt` es una copia
para poder filtrar su índice único. De paso, el recolector devolvía stock en orden de
producto y la compra lo aparta en orden de SKU: ahora los dos por SKU.

**Revisión por agente, ejecutando: 5 bugs reales** (`planning/27` §6). El peor, una carrera
entre reactivar la variante sin talla y añadir una talla que dejaba las dos formas activas
(23/25) → `sp_getapplock` por producto, con test que falla sin el lock. Los otros: 500 con
`variants:[null]`, SKU vacío, overflow del stock total, y el SKU del producto separándose del de
su variante. Y deadlocks por reintento al retirar mientras se compra → la compra ya no hace JOIN.

Verificado ejecutando: build limpio, **420/420**, 10 órdenes simultáneas por la última unidad
de una talla → 1 éxito, rollback de una línea ya apartada, migración aplicada y resembrado
(`194 variants`), OpenAPI regenerado.

### 2026-09-13 — Catálogo de demo de Teslo Shop, y la base local limpia

`planning/26`. El seed de la tienda Next.js entra como `Data/Seed/storefront-catalog.json`
(embebido) + 104 imágenes: `type` → categoría (`Shirts`/`Pants`/`Hoodies`/`Hats`), `gender` →
etiqueta, código de la imagen → SKU, slug del origen con guiones. Las imágenes suben por
`IFileStorage` en un único `SaveChanges`; no van al publish. Los tests apagan la demo.

🔴 **83 de las 104 imágenes eran WebP con extensión `.jpg`**: `LocalFileStorage` las rechazó
por firma y el primer arranque murió. Renombradas a `.webp` y vigilado por test.

Limpieza **autorizada por el owner, una sola vez** (§11.1): órdenes, pagos, catálogo, outbox,
inbox y `ExecutedCommands` vaciados (83.310 filas), identidades y secuencias
`OrderNumbers`/`PaymentReferences` reiniciadas, 2 imágenes y 93 comprobantes borrados.
**Usuarios, roles y `RefreshTokens` intactos** (20 usuarios).

Verificado ejecutando: build `-warnaserror` limpio, **390/390**, API arrancada → `Seeded 4
categories, 52 products and 104 images`, `GET /product/slug/mens-chill-crew-neck-sweatshirt`
200 con 2 imágenes servidas como `image/webp`, `/health/ready` Healthy, y segundo arranque
`Catalog already seeded, skipping` sin ficheros nuevos. `dotnet publish` sin las imágenes.

### 2026-09-13 — La IP del host vuelve: `192.168.3.76` → `192.168.3.82`

Solo configuración, igual que el cambio anterior del mismo día: `appsettings.Development.json`
(Redis y RabbitMQ) y `ConnectionStrings:ConexionSql` en user-secrets. El owner la confirmó
con `ipconfig getifaddr en0`; la `.76` ya no responde y la `.82` sí en los tres puertos.
Verificado ejecutando: build `-warnaserror` limpio, API arrancada → `/health/ready` `Healthy`
y las tres colas declaradas, y suite **385/385** con
`TEST_SQL_HOST=192.168.3.82 TEST_REDIS=192.168.3.82:6999`.

### 2026-09-13 — La IP del host cambió: `192.168.3.82` → `192.168.3.76`

Solo configuración: `appsettings.Development.json` (Redis y RabbitMQ) y
`ConnectionStrings:ConexionSql` en user-secrets (fuera del repo). La `.82` ya no responde.
Verificado ejecutando: build `-warnaserror` limpio, API arrancada → `/health/ready` `Healthy`
y topología de RabbitMQ declarada, y suite **377/377** con
`TEST_SQL_HOST=192.168.3.76 TEST_REDIS=192.168.3.76:6999`.

### 2026-09-12 — El catálogo se vuelve de tienda, y una migración que EF generó mal

`planning/24` paso 4, en **una sola migración** y con **autorización explícita del owner**:
la base es local y de desarrollo. Preguntar antes de esto es ahora `rules.md` §11.1, y esa
regla nació de esta misma conversación.

**Lo que entra**: `Slug` único con `GET /product/slug/{slug}`, tabla `ProductImages` (añadir
y quitar, hasta 8), `Tags` y `Sizes` como colecciones primitivas, **borrado lógico** y la
**FK `OrderItems → Products`** que faltaba.

🔴 **La migración que generó EF estaba mal en dos sitios**, y es la mejor defensa de la regla
«revisar la migración antes de aplicarla»:

1. Dejaba `Slug` a `''` en las **162 filas** existentes y **después** creaba el índice único.
   Habría reventado a mitad.
2. Tiraba la columna `ImageUrl` **antes** de copiar su contenido a `ProductImages`.

El arreglo es el **orden**: crear columnas → rellenar → tirar la vieja → crear los índices.
Y antes de aplicarla se comprobó en la base que no había líneas de orden huérfanas (habrían
hecho fallar la FK) ni nombres duplicados. Resultado: 162 productos, **162 slugs distintos**,
0 vacíos.

⚠️ **El borrado lógico trae una trampa de regalo**: los índices únicos de `SKU` y `Slug` hay
que **filtrarlos por `DeletedAt`**, o un producto retirado los bloquea **para siempre** y no
hay forma de liberarlos a mano, porque la fila es invisible.

⚠️ **Y una consecuencia que hay que conocer: dos productos ya no pueden llamarse igual.** El
slug sale del nombre y es único. Salió al migrar, porque el helper de tests creaba todos los
productos como «Producto de prueba». Si es deliberado se manda un `slug` explícito, y el 409
lo dice con esas palabras.

⚠️ **Lo que NO se trajo**: `gender` y `type` son vocabulario de una tienda de ropa concreta y
meterlos en el catálogo genérico hornea un vertical dentro de él; eso lo hacen `Category` y
las `Tags`. Y **las tallas son informativas: el stock sigue siendo por producto**, porque
hacerlo por variante cambia el contrato de la reserva entera.

**Verificado ejecutando**, contra la base real:

| Prueba | Resultado |
|---|---|
| Crear «Camión Ñandú SF» | slug `camion-nandu-sf` — sin acentos |
| `GET /product/slug/…` anónimo | 200 con el producto |
| Dos imágenes | salen en orden en `images[]` |
| **Vender y luego retirar** | **204** — con la FK, un borrado real habría fallado |
| Tras retirarlo | GET por id y por slug → 404; segundo DELETE → 404 |
| La fila y su línea de orden | **siguen en la base**, con `DeletedAt` puesto |
| Crear otro con el mismo nombre y SKU | **201** — el índice filtrado los liberó |
| Y otro más, con el primero vivo | **409** diciendo que mande un `slug` explícito |

Suite **377/377** (+12), build limpio con `-warnaserror`.

### 2026-09-12 — Lo que le falta a este backend para ser una tienda de verdad

`planning/24`. Comparación contra un e-commerce Next.js con panel de administración
(TesloShop) y su documento de migración a API separada. El diagnóstico cabe en una línea:
**las garantías sobran, falta superficie de tienda.** Todo lo que aquel pone como objetivo
difícil —reserva de stock en transacción, idempotencia, estados condicionales, jobs— aquí
estaba hecho y medido. Lo que faltaba eran endpoints que cualquier front da por sentados.

**1. `POST /cart/quote`, y la pieza que lo hace seguro.** El bug nº4 de aquel proyecto es el
impuesto calculado en **dos sitios** (0.15 en el cliente, 0.12 en el servidor, comparados con
igualdad exacta de floats). Escribir la cotización con su propio cálculo habría reproducido
ese bug **dentro de este repo**, así que primero nace `OrderPricing`: una función pura que
usan la cotización y `OrderService.BuildAsync`. Hay un test que compara los dos totales; si
alguien añade el IVA en un solo sitio, cae.

- ⚠️ **Cotizar no aparta stock**: `TryTakeAsync` reserva —correcto al comprar, veneno al
  cotizar— así que el puerto gana `PeekAsync`, de solo lectura. Medido: cotizar 5 de 5 dos
  veces deja el stock en 5.
- ⚠️ **Una línea sin stock es 200 con la línea marcada, no 409.** Diferencia deliberada con
  `POST /order`: el front tiene que poder enseñar «solo quedan 2». El que rechaza es el
  checkout.
- **Anónimo**: el carrito existe antes que la sesión.

**2. `PATCH /order/{id}/status`, y tres estados que llevaban meses muertos.** `Preparing`,
`Shipped` y `Delivered` estaban en el enum y **ningún camino llegaba a ellos**: una orden
tocaba `paid` y se quedaba ahí para siempre. Es el mismo defecto que `ReceiptStatus.Failed`
antes de `OnExhaustedAsync` — un estado inalcanzable es un enum que miente.

- **Sin `Idempotency-Key`, y es lo correcto**: la transición es un UPDATE condicional, así que
  reenviarla no repite nada. 0 filas → se relee: ya estaba en el destino → **204**; venía de
  otro estado → **409 `invalid_transition`**.
- **El origen lo pone el servidor**, no la petición: si viniera del cliente se podría saltar
  un paso pidiendo `delivered` desde `paid`.
- ⚠️ **No emite ningún evento**, por la misma razón que colocar una orden: nadie consume
  `order.shipped` y publicar sin cola vuelve como 312 NO_ROUTE.
- **Cancelar y reembolsar quedan fuera a propósito** (deuda ya registrada en `docs/06`).

**3. Contadores del panel, uno por contexto.** ⚠️ Aquí se **rechaza** el diseño del documento
de origen: un `GET /admin/dashboard` único obligaría a un slice a conocer a otros tres, o a
inventar un sexto contexto cuyo dominio es *una pantalla*. Cada contexto publica los suyos
(`/order/stats`, `/product/stats`, `/user/stats`) y el panel compone con tres llamadas en
paralelo. `lowStock` **excluye el 0**: mezclarlo con lo agotado hace que el panel pida
reponer lo que ya no se puede vender.

**Verificado ejecutando**, contra SQL Server y Redis reales:

| Prueba | Resultado |
|---|---|
| Cotizar 5 de 5, dos veces | stock **sigue en 5** |
| Cotizar 10 de 5 + un SKU inexistente | 200, `insufficient_stock` / `not_found`, total **0** |
| Cotizar y comprar el mismo carrito | **59.97 = 59.97** |
| `placed → preparing` | **409 `invalid_transition`** |
| destino `paid` | **400** enumerando `preparing, shipped, delivered` |
| **8 administradores simultáneos** sobre la misma transición | **8×204**, estado final `preparing`, una sola escritura |
| `preparing → shipped → delivered`, y repetir | 204, 204, y **409** (entregada es final) |
| Los tres `/stats` con un usuario sin rol | **403, 403, 403** |

Suite **359/359** (+37), build limpio con `-warnaserror`.

⚠️ **Lo que queda de esa comparación** (pasos 4–6 de `planning/24`): el catálogo no tiene
`slug`, ni varias imágenes, ni tallas, ni tags, ni borrado lógico; la dirección de envío es un
`string(500)`; y falta PayPal. Sobre PayPal hay un hallazgo: `IPaymentGateway` tiene
`CreateIntentAsync` y `ParseEvent`, o sea que **está hecho a la medida del modelo de Stripe**
—crear, confirmar, webhook— y PayPal necesita un tercer paso, **capturar desde el servidor**,
que hoy no cabe en el puerto.

### 2026-09-12 — Fuera AutoMapper: el mapeo se comprueba al compilar

`planning/23`. Empezó como una revisión de licencias del stack y acabó en un cambio
técnico, porque el dato que teníamos estaba mal.

**El dato falso.** El repo decía en cuatro sitios que AutoMapper 15 «exige licencia
comercial en producción». Verificado contra la fuente: es dual **RPL-1.5 + comercial** y
tiene **Community gratuita por debajo de 5 M USD**. No había que pagar, había que
registrarse — y lo que la nota tapaba era el riesgo real: sin registrar nada, aplica
RPL-1.5, que es **copyleft recíproco**.

**Por qué se migra igual, y no por la licencia.** `Riok.Mapperly` es un **source
generator**: el mapeo es C# escrito al compilar, sin reflexión, y un miembro del destino sin
alimentar es un **RMG020**, o sea un error de build con `-warnaserror`. Con AutoMapper eso
era una excepción en la primera petición, o un test que había que acordarse de escribir. Y
este repo ya pagó un **500 en producción** por exactamente esa clase de fallo.

**La pieza que faltaba.** `CrudService` es genérico y llamaba a `IMapper.Map<TDto>(...)`, que
un generador no puede resolver. Nace `IEntityMapper<TEntity,TDto,TCreateDto,TUpdateDto>`
(`Shared/Crud/`), gemelo de `IEntityRules<,,>`: mecanismo genérico, traducción inyectada.
Sin genérico abierto por defecto a propósito — no existe un mapeo «vacío» razonable, así que
una entidad sin mapeador **rompe el arranque**, que es donde se quiere que rompa.

**Lo que se genera y lo que no.** Las proyecciones se generan; el PATCH se escribe a mano,
campo a campo. Mapperly tiene `AllowNullPropertyAssignment = false` documentado justo para
eso y **no se usa**: el PATCH es donde el repo ya se quemó, y siete líneas de
`entity.X = dto.X ?? entity.X` no hay que ir a verificarlas en la documentación de nadie.

**Tres cosas que aparecieron, y las tres las dijo el compilador o el código generado**

- 🟠 **Dos RMG020 en el primer build**: `Category.CreatedAt`/`UpdatedAt` no llegan a
  `CategoryDto`. Es correcto —ese DTO no expone auditoría, a diferencia de `ProductDto`— y
  ahora está **declarado** con `[MapperIgnoreSource]` en vez de ser un silencio.
- ⭐ **La duda que quedaba se resolvió leyendo el `.g.cs`**, no ejecutando: el generado es
  `target.CategoryName = entity.Category?.Name`, null-safe, así que un GET que olvide el
  `.Include` sigue devolviendo null y no una excepción. Y `ToBase64` se engancha **por
  firma** (`byte[]?` → `string?`) sin configurar nada.
- **Desaparece `AddObjectMapping`**, y con ella una de las dos excepciones documentadas a
  «`Shared/` no nombra tipos de `Features/`»: ya no hay ensamblados que escanear, así que el
  composition root dejó de necesitar `typeof(CategoryProfile).Assembly`.

⚠️ **Hallazgo lateral**: el `Trim` del SKU es **inalcanzable por HTTP**. La DataAnnotation
del DTO rechaza espacios antes de que el mapeador vea el valor. Se deja como defensa en
profundidad, y el test unitario lo cubre.

**Verificado ejecutando**, contra SQL Server y Redis reales, no solo compilando:

| Prueba | Resultado |
|---|---|
| POST con `"  Producto MIG  "` | `"Producto MIG"` — Trim aplicado |
| GET del producto | `categoryName: "Bebidas MIG"`, `rowVersion: "AAAAAAACgKI="` |
| **PATCH solo con `name`** | `categoryId` 6092, `price` 99.9, `stock` 10, `sku` **intactos** |
| PATCH con `stock: 0` | stock 0 — un 0 explícito **sí** se aplica |
| PATCH con `description: ""` | queda `""`; `imageUrl`, no enviado, sigue null |
| PATCH de categoría solo con `description` | el `name` aguanta (pasa por el decorador de cache) |
| `GET /product/paged` | 2 items de 162, con `categoryName` resuelto |
| Arranque en `Production` sin Redis ni broker | `/health` 200, `/health/ready` Healthy, 0 errores |

Suite **322/322** (+4: cinco nuevos de Trim y rowversion, menos el
`Configuration_IsValid`, que ahora lo hace el compilador). Build limpio con `-warnaserror`.
`notes.md` capítulo 44.

### 2026-09-12 — Verificar el estado, y el warning que decía que no era verdad

Sesión de estado, no de feature. Lo único que se tocó de código es una línea, y esa línea
dice algo sobre el proceso:

- **`OrderService` seguía recibiendo `IEventOutbox`** después de que `planning/22` dejara de
  emitir `order.placed`. Warning **CS9113** (`Parameter 'outbox' is unread`), o sea que el
  build **no** estaba a 0 warnings aunque el commit anterior lo afirmara.
- ⚠️ **Por qué nadie lo vio**: `-warnaserror` **no está en el `.csproj`**. Es un flag que
  solo pone la CI, y la CI está desactivada desde el 2026-09-07. Un `dotnet build` normal
  imprime el warning y sigue con `Build succeeded`. La regla de «0 warnings» lleva desde
  entonces sostenida por que alguien mire la salida.
- Fuera la dependencia y los dos `using` que quedaban colgando de ella.

**Medido**: `dotnet build -warnaserror` → 0 warnings; `dotnet test` → **318/318** en ~16 s
contra SQL Server, Redis y RabbitMQ reales. Las dos direcciones de la infra responden
(`192.168.3.82` y `172.17.0.1`, los seis puertos).

Y se corrigió el punto de continuación de `memory.md`, que decía que **`Payments` no
existía** y daba 274 tests: ese archivo se lee al empezar **toda** sesión, así que una
afirmación falsa ahí contamina todas a la vez.

**`notes.md` capítulo 43 — dónde viven los secretos**, con la ruta exacta
(`~/.microsoft/usersecrets/apiecommerce-dev-2026/secrets.json`), la cadena de capas de
configuración (`:` → `__`) y la respuesta medida a «¿y si los pongo en `./secrets/`
gitignorado?»: el Web SDK copia **todo `**/*.json` del proyecto** a `bin/` y a `publish/`, o
sea **dentro de la imagen** — y el `.dockerignore` solo cubre el nombre `secrets.json`, no
`secrets/local.json`. Decisión del owner: **se queda en user-secrets**, porque estar fuera de
la raíz del repo no es una exclusión que `git add -f` pueda vencer.

### 2026-09-07 — Pagos: quinto contexto acotado, y la orden deja de nacer pagada

`planning/22`. Decisión del owner: **Stripe de verdad**, pero con la elección de proveedor
por petición para que PayPal quepa después; y la orden **nace `Placed`**, no `Paid`.

**Las tres decisiones que lo ordenan**

1. **La orden nace sin pagar.** Antes `OrderService` la dejaba `Paid` al colocarla, que es
   cómodo y mentira. Ahora el único que puede moverla es un cobro capturado por la pasarela.
   Es lo que hace de Payments un contexto acotado y no una tabla más.
2. **El proveedor lo elige el comprador → Strategy + factory.** ⚠️ Va **contra** la regla
   general del repo (`CLAUDE.md` §5.4: la implementación de un puerto se elige una vez en el
   composition root) y es consciente: allí quien elige es la *infraestructura*, aquí quien
   elige es *cada petición*. `PaymentGatewayRegistry` resuelve `IEnumerable<IPaymentGateway>`
   por `Provider`, así que **añadir PayPal es una clase y un `AddSingleton`**.
3. **El webhook es la única fuente de verdad del cobro.** El 201 de `POST /payment` solo
   dice «la pasarela aceptó el intento». Nadie marca un pago como cobrado por haber llamado
   a nuestra propia API.

**El comprobante se muda de evento.** Colgaba de `order.placed`, o sea que emitía el PDF de
una compra que nadie había pagado. Ahora cuelga de `order.paid`. Y ⚠️ **colocar una orden ya
no emite ningún evento**: nadie consumiría `order.placed`, y publicar sin cola que lo acepte
vuelve como **312 NO_ROUTE** y agota el outbox en silencio (trampa ya documentada). Volverá
el día que Shipping o las notificaciones lo escuchen.

**La deuda que crea, y que se cierra en el mismo paso.** Una orden `Placed` retiene stock:
`AbandonedOrderCleaner` (sobre el `PeriodicBackgroundService` de ayer) la cancela pasada
`Payments:ReservationMinutes` y devuelve el stock **en la misma transacción que la
transición**, que es lo que lo hace idempotente.

**Tres cosas que aparecieron al escribirlo, y ninguna la habría visto compilando**

- 🔴 **Un webhook firmado podía provocar un 500.** `Stripe.net` lanza
  `NullReferenceException` —que **no** es `StripeException`— si el cuerpo no trae
  `api_version`; y otra al leer `Data.Object` si no trae `data`. Con el `catch` estrecho
  salían como 500, o sea diciéndole a quien mandó el cuerpo que ha encontrado algo. Todo lo
  que pasa ahí es sobre un cuerpo que manda cualquiera: cualquier fallo es petición mala.
- 🟠 **Un fallo de Stripe salía como `internal_error` 500.** Que la pasarela no conteste no
  es un fallo nuestro y es reintentable: ahora es **503 `payment_gateway_unavailable`** con
  `Retry-After`, y el motivo real se queda en el log porque puede nombrar la clave.
- 🟠 **El 503 de «no hay pasarela» se registraba como incidente.** Se daría en *toda*
  petición de un despliegue que no cobre. El corte del nivel de log pasa a ser «¿lo
  decidimos nosotros?» y no «¿es 5xx?»: una `AppException` es `Warning`, lo no mapeado sigue
  siendo `Error` con traza. Es el mismo problema que costó 10 incidentes falsos con los 409.

Y una lección escribiendo los tests: llamar al handler de `payment.captured` a pelo no
persistía nada, porque el efecto encola con `Add` sin `SaveChanges` **a propósito**. El test
tiene que entrar por `IMessageInbox`, que es lo que hace la transacción — o sea que la forma
correcta de probarlo es la forma correcta de ejecutarlo.

**Verificado ejecutando**, contra SQL Server, Redis y RabbitMQ reales:

- Topología declarada: `apiecommerce.order-paid` y `apiecommerce.order-payment`, cada una con
  su retry y su DLQ propias.
- Orden colocada → **`placed`**, sin comprobante, con el stock ya apartado (5 → 3).
- `POST /payment` sin Stripe configurado → **503 `no_payment_provider`**; proveedor
  inexistente → **400** enumerando los disponibles.
- Cadena completa por el broker: `payment.captured` → **orden `paid`** → `order.paid` →
  **comprobante `available`**, PDF de 29.332 bytes descargado, **DLQs en 0**.
- Llamada **real a Stripe** con clave inválida → **503 `payment_gateway_unavailable`** con
  `Retry-After: 1`, motivo solo en el log, y **0 filas** en `Payments`: la transacción se
  deshizo entera, que es justo la garantía que se buscaba.
- Suite **318/318** (+30), build limpio con `-warnaserror`.

⚠️ **Lo único que NO se pudo verificar aquí**: el camino feliz contra Stripe de verdad, que
necesita una `sk_test_…` del owner, y la entrega de un webhook real, que necesita una URL
pública. La verificación de firma sí está probada, firmando a mano con el mismo esquema.

### 2026-09-07 — Los menores de la revisión: nueve arreglos pequeños, ninguno cosmético

Cierra la deuda que quedaba de la revisión multiagente antes de abrir `Payments`.

1. **`PeriodicBackgroundService`** (`Shared/Hosting/`) — el bucle de un job periódico estaba
   copiado **cuatro veces**. Lo que se copiaba no era estética: el `catch (Exception)` **sin
   filtro** es lo único que impide que una excepción mate el servicio y, con `StopHost` por
   defecto, tumbe la API. `ExecuteAsync` queda `sealed` para que ninguna subclase se lo salte.
   `ReceiptCleaner` pasa de 90 líneas a 39; `RefreshTokenCleaner` de 78 a 51.
2. **`RefreshTokenCleaner` inyectaba `IOptions<RefreshTokenOptions>` y no lo leía nunca.** Fuera.
3. **`CacheKeys` → `Features/Catalog/CatalogCacheKeys`** — vivía en `Shared/Caching/` nombrando
   vocabulario del catálogo, y **tres de sus cinco miembros estaban muertos** (`ProductAll`,
   `Product(id)`, `ProductsByCategory(id)`: solo se cachean categorías).
4. **`IFileStorage.SaveProductImageAsync` → `SaveImageAsync`** — un puerto de `Shared/` no
   nombra una entidad de un slice. La carpeta sigue saliendo de `Storage:ProductImagesFolder`,
   que es configuración, no tipo.
5. **`IdentityMapping`** (`Features/Accounts/`) — `ToDto` estaba duplicado letra por letra en
   `AuthService` y `UserAdminService`, y las dos copias **habían divergido al traducir
   `IdentityResult`**: una agrupaba por campo y la otra metía todo bajo una clave inventada
   `identity`. Eran **dos 422 con forma distinta** según qué endpoint fallara.
6. **`AuthController` fijaba `version = "1.0"`** en el `CreatedAtRoute` del registro: con una
   v2, el `Location` habría apuntado a la v1. Ahora sale de `HttpContext.ApiVersionValue()`.
7. **`IReceiptRenderer` se muda de `Service/` a `Documents/`**, junto a su implementación: es
   infraestructura del slice, no una regla de negocio. (No a `Ports/`, que aquí significa
   «lo que habla con otro slice».)
8. **El comentario de `RateLimitPolicies` mentía**: decía que resolver `IOptions<T>` por
   petición respeta «un cambio en caliente». `IOptions<T>` es un singleton que se resuelve una
   vez; para recargar haría falta `IOptionsMonitor<T>`, y con él un valor inválido recargado
   rompería cada petición en vez de fallar al arrancar. Corregido el comentario, no el código.
9. **Siete `using` muertos**, verificados **compilando uno a uno**: de 60 candidatos que dio el
   análisis estático, 53 eran falsos positivos (métodos de extensión y `<see cref>`). Vale la
   pena anotarlo: aquí un análisis de «usings sin usar» acierta el 12%.

Verificado ejecutando, porque el punto 1 toca los cuatro servicios de fondo: orden colocada →
`pending` → **`available` en 10 s** con PDF de 28.667 bytes (o sea outbox, broker, consumidor e
inbox siguen funcionando); arranque con `Documents__CleanupIntervalHours=0` → la rama de apagado
del recolector registra `orphan collection disabled` y la API arranca igual; 0 errores en los dos
logs. Suite **288/288**, build limpio con `-warnaserror`.

### 2026-09-07 — La envoltura de idempotencia deja de estar copiada, y fuera dos piezas muertas

Primera pieza de la deuda que dejó la revisión multiagente, y la que más pesaba: la
envoltura «marca del intento + efecto en la misma transacción» estaba **copiada íntegra**
en `ProductService.BuyAsync` y en `OrderService.PlaceAsync` — apertura de transacción,
consulta del intento previo, `Record`, `SaveChanges` final y el
`catch (…) when (IsDuplicateIntent(ex))` con su mensaje incluido. Unas 20 líneas que no son
de conveniencia sino de **garantía**: al tercer caso de uso, basta con copiar mal el `catch`
para que un duplicado concurrente salga como 500.

- **`IIdempotentCommandRunner` / `IdempotentCommandRunner`** (`Shared/Idempotency/`), scoped.
  Gemelo declarado de `IMessageInbox`: el mismo patrón de «exactamente una vez», uno para
  comandos entrantes por HTTP y otro para mensajes entrantes por el broker.
- **El efecto no hace el `SaveChanges` final**, lo hace el runner junto a la marca. Los
  intermedios sí (`OrderService` necesita el `Id` generado para el evento) y van en la
  misma transacción.
- Los dos servicios adelgazan: `ProductService` pasa de 8 dependencias a 7 y `OrderService`
  de 7 a 6, y las dos `PlaceAsync`/`BuyAsync` dejan de ser `async` — solo devuelven la
  llamada al runner.

**Retirado por muerto, en el mismo barrido:**

- **`BaseRepository.ExistsByFieldAsync`** — cero llamadas, reflexión sobre el modelo de EF, y
  **fallaba en abierto**: si la propiedad no existía o no era `string` devolvía `false`, o sea
  «no hay duplicado». Una errata en el nombre del campo desactivaba la regla en silencio.
- **`Shared/Db/TransactionalAttribute.cs`** (56 líneas) — aplicado a **cero** acciones.
  `ActionExecutionDelegate` no es reentrante y con `EnableRetryOnFailure` ejecutaría la
  acción dos veces. La lección se conserva en `docs/07`, ahora como alternativa descartada.

⚠️ Y al retirarlo aparecieron **dos documentos que mentían**: `docs/00` y `docs/06` seguían
diciendo que `[Transactional]` estaba «aplicado a `POST /api/v1/product/buy`». Es exactamente
la clase de afirmación falsa que ya se cazó una vez (bitácora del 2026-09-07, limpieza de
comentarios) y que había sobrevivido en otro archivo.

Verificado ejecutando, porque toca concurrencia e idempotencia: **30 compras simultáneas
sobre stock 20 → 20×201 + 10×409, 20 números de orden únicos, stock final 0**; la misma
`Idempotency-Key` dos veces en `POST /order` → misma orden `ORD-2026-000091` con
`Idempotency-Replayed: true` y stock descontado **una** vez; lo mismo en `POST /product/buy`;
clave reutilizada con otro cuerpo → **422**. Comprobantes: 25/25 `available`, PDF de 27.638
bytes descargado, **DLQ en 0**, 0 líneas de error en el log. Suite **288/288** (+6, todos del
runner), build limpio con `-warnaserror`.

### 2026-09-07 — Listado de administración de órdenes (`GET /order/all`)

Lo pidió el owner después de mirar los logs y no ver ninguna orden: `GET /order/paged`
filtra por comprador (`Where(o => o.BuyerUserId == …)`), estaba entrando con un usuario
recién registrado y las 70 órdenes de la base son de `admin`. El `200` con página vacía era
correcto; lo que faltaba era la vista de administración.

- **`GET /api/v1/order/all`** — paginado sobre todas las órdenes, con `?number=` que filtra
  por **prefijo** del número. Prefijo y no subcadena: un `LIKE '%x%'` no puede usar
  `IX_Orders_Number` y degrada a recorrido completo.
- **Ruta aparte, no un parámetro de `/paged`.** Un `?all=true` haría que la autorización
  dependiera de que un filtro esté bien puesto; con dos rutas, el `[Authorize(Roles = admin)]`
  es lo único que las separa y no hay forma de que un listado propio se vuelva global.
- **`OrderDto.BuyerUserId`** — el listado de admin no sirve si no dice de quién es cada
  orden. Es aditivo: en el listado propio es el id del que pregunta, que ya conoce.
- **Índice `IX_Orders_PlacedAt`** (migración `20260907012548_OrdersAdminListingIndex`). El
  índice que había es `(BuyerUserId, PlacedAt)` y no sirve para ordenar sin filtrar por
  comprador: sin este, el listado global era recorrido completo más ordenación.

⚠️ Volví a pisar la trampa de `dotnet ef migrations add --no-build`: generó una migración
**vacía** porque usó el ensamblado anterior a editar `AppDbContext`, y el `migrations remove`
—también con `--no-build`— intentó borrar la migración *anterior*, que ya estaba aplicada.
Está documentada en `CLAUDE.md` §10 y aun así la usé. Rehecho compilando primero.

Verificado ejecutando contra SQL Server real: admin ve **70 órdenes / 24 páginas**,
`?number=ORD-2026-000042` devuelve exactamente una, un número inexistente devuelve `200`
con `[]` (no 404), usuario normal **403**, anónimo **401**, `pageSize=1000` **400**,
`page=2147483647` **200** (la corrección de `PageQuery.SkipFor` aguanta), `number` de 80
caracteres **400**. El listado propio sigue devolviendo 0 para un usuario ajeno.
Suite **282/282** (+4), build limpio con `-warnaserror`, 0 errores en el log del arranque.

### 2026-09-07 — Limpiar el codigo, y las cinco cosas que la revision destapo

El 40 % del codigo fuente eran comentarios (5.241 de 13.200 lineas): `<remarks>` de tres
parrafos para explicar un semaforo, emojis por todas partes, la historia de cada cambio
incrustada entre las lineas que pretendia explicar. Peticion del owner, y con razon: eso no
documenta, **tapa**.

**Comentarios: 5.241 → 3.215 (−39 %)**, ratio del 40 % al 28 %, y **cero iconos**. El
contrato de estilo queda en `rules.md` §1.1 para que no se vuelva a contaminar, y **no se
perdio nada**: el porque largo vive en `docs/07-decisiones-en-el-codigo.md`, 174 secciones
indexadas por ruta de archivo (las 174 rutas verificadas: todas existen). Seis agentes en
paralelo, uno por area, con el mismo contrato para que no divergieran.

Verificado con una maquina de estados que entiende literales de C#: **en los 194 ficheros
solo cambiaron comentarios**, ni una linea de codigo ejecutable.

**Y despues, tres revisiones en paralelo — arquitectura, operacion real y calidad de la
limpieza— encontraron mas de lo que esperaba.** Lo de operacion es lo que duele:

- 🔴 **Con Redis caido no se degradaba, se caia.** Los timeouts estaban puestos, pero la
  `BacklogPolicy` por defecto **encola** los comandos mientras la conexion esta caida y cada
  uno espera su timeout entero —y una lectura cache-aside hace dos llamadas—. Medido: `GET
  /category` pasaba de 3 ms a **3,4–4,0 s**, con `/health/ready` devolviendo 200, o sea el
  orquestador mandando trafico a replicas que tardaban cuatro segundos. Con `FailFast`: 5 ms.
- 🔴 **El rate limiter de `auth` se esquivaba con una cabecera.** `KnownNetworks.Clear()`
  hacia confiar en `X-Forwarded-For` de cualquiera. Rotandola, fuerza bruta sin limite.
  ⚠️ Y no se reproduce desde localhost, porque el cliente **es** un proxy de confianza por
  defecto: hay que probarlo desde una IP no-loopback.
- 🔴 **La CI no podia arrancar SQL Server**: tres lineas de comentario estaban DENTRO del
  escalar plegado `options: >-`, y en YAML eso es contenido, no comentario.
- 🔴 **Dos carreras**: cuatro admins degradandose a la vez dejaban **cero administradores**
  sin vuelta atras por la API, y seis registros del mismo email creaban dos cuentas y dejaban
  ese email dando **500 permanente** (`FindByEmailAsync` hace `SingleOrDefault`).
- 🔴 **El desbordamiento de paginacion seguia vivo**: lo di por arreglado en `PageQuery`,
  pero los repositorios **no usan `Skip`**, repiten la formula en `int`.

✏️ **Y una correccion mia de bulto**: al arreglar el desbordamiento la vez anterior toque
`PageQuery` y no comprobe quien lo usaba. Nadie. El 500 seguia ahi y yo lo habia dado por
cerrado en un commit.

Ademas: los indices unicos de SKU y Name **no se usaban** (la columna iba envuelta en
`ToLower().Trim()`, lo que impide el seek), la validacion del grafo de DI estaba apagada en
Testing y Production, MARS metia **136 de 450 lineas de log** en el camino feliz, y el compose
no tenia limites de recursos, rotacion de logs ni volumen para las claves de DataProtection.

Todo verificado ejecutando, no compilando. **278 tests** en verde.

### 2026-09-06 — La DLQ deja de ser un callejón sin salida

`planning/21`, y cierra las dos deudas que dejó abierta `planning/20`. Son **dos mitades del
mismo problema**: que un efecto pueda fallar sin dejar ni trabajo perdido ni basura.

**El agujero era mío, de ayer.** Cuando un comprobante agotaba sus reintentos, el aviso de
agotado marcaba la orden como `failed` y el cliente dejaba de esperar —eso estaba bien—,
pero **reemitirlo exigía entrar a la consola del broker**. Una cola de la que no se sale no
es una red de seguridad, es un vertedero.

- **`GET /api/v1/dead-letter`** dice cuántos hay parados en cada cola, y
  **`POST /{queue}/replay`** los devuelve a la principal. ⚠️ La cola llega **en la petición**,
  así que se resuelve contra las suscripciones **registradas**: es una allowlist por
  construcción, y sin ella el endpoint movería mensajes de cualquier cola del broker — que
  es compartido con otros proyectos. Cola desconocida → 404.
- ⚠️ **Reemitir es una decisión humana, no un job.** Si algo agotó sus intentos es porque
  estaba roto de verdad; automatizarlo convierte la DLQ en un bucle caro que además esconde
  el incidente.
- Se reutilizan dos reglas que ya estaban escritas: **publicar antes de confirmar** (al
  revés, morir entremedias pierde el mensaje) y **el contador de intentos a cero** — que es
  exactamente por lo que el contador es nuestro y no `x-death`, que sobrevive al paso por la
  DLQ y habría hecho que la herramienta de recuperar mensajes no recuperara ninguno.
- Sin broker, el Null Object falla **en cerrado** con un 503. Es la distinción de
  `rules.md` §8: los otros Null Object degradan en abierto porque envuelven optimizaciones;
  aquí «no hay mensajes muertos» sería **mentira**, no degradación.

**Y el recolector de huérfanos**: el PDF se escribe dentro de la transacción y un fichero no
se deshace con ella, así que un commit fallido deja basura que nadie apunta.
- ⚠️ 🔴 **El periodo de gracia es la única línea que no se puede equivocar.** El fichero
  existe *antes* que la fila que lo apunta, así que sin corte el recolector borraría
  comprobantes **buenos a mitad de vuelo** — y eso no se recupera. 24 h por defecto, tres
  órdenes de magnitud por encima de la ventana real.
- **Ante la duda, no se borra**: si la base no contesta, se salta el lote entero. La
  asimetría del coste decide sola.
- ✏️ Un PDF **truncado** no necesita caso especial, al contrario de lo que decía
  `planning/20` §20.11: como `SaveAsync` nunca devolvió clave, nadie lo referencia y cae por
  la misma regla. Queda corregido.
- El efecto vive **fuera** del `BackgroundService` (la lección de `planning/18`), y aquí eso
  importa más que en ningún sitio: este componente **borra ficheros**, y los tests que hacen
  falta son los dos que comprueban que *no* borra.

**Verificado ejecutando** el ciclo completo contra RabbitMQ real: comprobante que muere →
orden `failed` → el endpoint lo ve (2 en la DLQ de órdenes, 0 en la del catálogo) → cola
desconocida da 404 → se arregla el almacén → `{"replayed": 2}` → la orden pasa a
`available` y el PDF baja. Las dos DLQ a cero y **0 errores** en el log.

**274 tests** en verde (eran 262).

### 2026-09-06 — Regenerar la documentación, y lo que eso destapó

`CLAUDE.md` se carga en **toda** sesión de agente, así que cada afirmación falsa contamina
todas a la vez. Una auditoría contra el código encontró **~20**: carpetas que no existen
(`Models/Dtos/`, `Service/Crud/`, `Service/Auth/`), nombres del composition root inventados
(`AddApplication`/`AddInfrastructure`), «`[Transactional]` está aplicado a `POST /buy`»
cuando no lo lleva ningún endpoint, y —la peor— **«no hay proyecto de tests: `dotnet build`
es el único check»** con 261 tests y CI en verde.

Reescrito entero contra el código, con cuatro agentes levantando el inventario en paralelo
(Shared, slices, configuración/despliegue, auditoría) y **un quinto intentando refutar el
resultado**. Ese último no encontró ninguna afirmación falsa y sí cuatro imprecisiones, las
cuatro corregidas.

**La decisión que evita la recaída**: una sola fuente de verdad por dato, escrita en
`memory.md` §6.ter. Los **conteos volátiles no van en `CLAUDE.md`** —quedan obsoletos y
nadie lo nota—; van en `progress.md` y `memory.md`, que se actualizan en el mismo commit que
el código. `docs/01`–`05` conservan su razonamiento, que es lo valioso y lo que ningún
documento regenerado reproduce; lo que iba caduco eran rutas, nombres de método y números.

🔴 **Y verificar la documentación destapó un bug de código**: el orden por defecto de
`BaseRepository` no tenía **desempate estable**. `CreatedAt` no es único —lo estampa
`DateTime.Now`, y el seeding crea cinco categorías en el mismo tick—, así que el orden no
era total; como cada página es un `OFFSET/FETCH` independiente, una fila podía salir en dos
páginas y otra en ninguna. La regla estaba escrita a mano en los repositorios que paginan de
verdad (`ProductRepository`, `OrderRepository`) y **faltaba justo en el camino genérico**,
que es el que sirve `GET /api/v1/category/paged`. Arreglado con un `ThenByDescending` por
clave primaria y un test que pagina 12 categorías creadas seguidas comprobando que no hay
duplicados ni pérdidas.

También se regeneró el `README.md` de la raíz, que seguía siendo un borrador de comandos
sueltos desde los primeros commits.

**262 tests** en verde.

### 2026-09-06 — Órdenes, y el comprobante que no se genera en la petición

`planning/20`, y lo primero: **`Ordering` es el cuarto contexto acotado** y el primero que
se añade con el slicing ya asentado. Costó lo que tenía que costar — crear la carpeta y una
línea en `AddFeatures()` — y toda la dependencia hacia `Catalog` cabe en **una** clase
(`CatalogGateway`), que es la prueba de que el puerto valía la pena.

**El diseño lo ordenan tres frases**: el PDF **no se genera en la petición** (evento por el
outbox y un consumidor aparte, porque una compra ya cobrada no puede depender de que el
generador esté vivo); la base guarda una **clave opaca** y no una ruta (si guardara
`/app/App_Data/.../x.pdf`, migrar a S3 obligaría a reescribir todas las filas); y lo que se
copia en la orden **se congela**, porque un documento que cambia cuando cambia el catálogo
no es comprobante de nada.

**Dos almacenes de ficheros, y no se fusionan.** `IFileStorage` guarda imágenes *dentro* de
`wwwroot` para que `UseStaticFiles` las sirva a cualquiera: son públicas y esa es su gracia.
Un comprobante lleva el nombre del cliente, su dirección y lo que pagó. La pregunta que los
separa no es «¿qué hace?» sino **«¿quién puede leerlo?»**. Tres barreras, y hacen falta las
tres: fuera de `wwwroot`, clave aleatoria de un CSPRNG, y el endpoint comprobando de quién
es la orden.

🔴 **Lo que había que resolver por debajo, y no era código de órdenes**: `Shared/Messaging`
servía a **una** cola. Publicar un segundo evento sin tocarlo fallaba en silencio — el
publicador usa `mandatory: true` con confirms, así que `order.placed` volvía como **312
NO_ROUTE**, el outbox lo contaba como intento fallido y se agotaba: la compra funcionaría y
el comprobante **no se generaría nunca**. Ahora cada slice declara su `EventSubscription` y
la fontanería AMQP se hereda de `EventConsumer<TConsumer,TEvent>` — 200 líneas que, copiadas,
habrían dejado los arreglos de `planning/18` en una sola de las dos copias.
⚠️ Con dos cuidados que son bugs evitados, no estética: la cola del catálogo se declara con
sus argumentos **exactos de hoy** (cambiar su `x-dead-letter-exchange` da 406 y tumba la
mensajería) y cada slice nuevo lleva **su propia DLX**, porque la heredada es `fanout` y
repartiría los mensajes muertos de uno a la dead-letter del otro.

🔴 **Y midiendo apareció algo anterior a esta tarea**: cada 4xx de dominio escribía un
«An unhandled exception has occurred» a nivel **Error y con traza completa**. Medido: 30
compras simultáneas sobre stock 20 → 20 órdenes correctas y **10 incidentes falsos**, uno
por rechazo legítimo. Un 404 de categoría hacía lo mismo, o sea que venía de antes y a
`planning/19` se le escapó — aquello miró los cortes de cliente y los timeouts de base, no
las excepciones de dominio. La línea del framework era además **un duplicado**:
`GlobalExceptionHandler` ya registraba todo, y mejor. Silenciada por configuración: de 10
«errores» a **0**.
⚠️ `SuppressDiagnosticsCallback` es de .NET 10; aquí hay que hacerlo con un
`MinimumLevel:Override` a `Fatal`, porque Serilog no tiene nivel `None`. Y dentro de
`Override` **no caben comentarios `//`**: Serilog resuelve cada clave como nombre de logger
y el arranque muere. Lo cazó ejecutando, no compilando.

✏️ **Y dos correcciones propias, las dos vistas ejecutando y no leyendo.** El comprobante se
descargaba con el nombre de la clave opaca (`cdfdcf87….pdf`): el almacén devolvía lo único
que sabe, pero cómo se llama un documento **de cara al usuario** es del dominio. Y se pedía
la fuente Calibri, que **no existe en Linux** — se quita, y el documento usa la que QuestPDF
**embebe**; verificado en el PDF: A4 y tres subsets de Lato dentro, así que sale igual en
local que en la imagen.

**Verificado ejecutando** contra SQL Server, Redis y RabbitMQ reales: el ciclo entero compra
→ evento → PDF (31 KB), 30 compras simultáneas dando **20 números de orden únicos**, 22
órdenes con sus 22 comprobantes, la misma `Idempotency-Key` devolviendo la misma orden, y
409/404/401 donde tocan.

**Y la revisión multiagente (`rules.md` §9) encontró cinco cosas que ni el build ni los 246
tests veían**, dos de ellas serias: 🔴 **`ReceiptStatus.Failed` era inalcanzable** —el estado
existía, estaba migrado y no lo escribía nadie— así que un comprobante muerto en la DLQ
dejaba al cliente con un 409 `receipt_not_ready` **para siempre**, o sea haciendo polling
sobre un documento que no iba a existir; y 🔴 **un deadlock evitable**, porque el stock se
descontaba en el orden del carrito (A compra `[1,2]`, B compra `[2,1]`, cada uno bloquea el
primero y espera el del otro). Se arreglan con un hook `OnExhaustedAsync` —el único momento
en que «ya no habrá más intentos» es cierto— y ordenando las líneas por SKU, que hace el
deadlock imposible por construcción. Más: la canonicalización de rutas **no seguía enlaces
simbólicos** aunque el comentario decía que sí (reproducido por el revisor), una barra final
en `Documents:RootPath` rompía el almacén entero en silencio, y `?page=2147483647` daba un
**500** en todos los `/paged` por desbordamiento — este último, previo.

✅ **Y algo que se daba por no verificable, se verificó**: el ciclo de reintentos con la
generación fallando de verdad. No hacía falta tumbar SQL Server, bastaba una raíz de almacén
sin permiso de escritura. Medido: 2 intentos → DLQ → la orden pasa a `failed` → 409
`receipt_failed`, **con la compra intacta** (`paid`, total y líneas), y el mensaje muerto en
la DLQ de órdenes y **no** en la del catálogo.

**261 tests** en verde (eran 202), build sin warnings.

### 2026-09-06 — Administrar usuarios, y el agujero que eso destapó

`planning/14`: listado paginado, dar y quitar roles, bloquear y desbloquear. Todo con
`UserManager` y **no** con el CRUD genérico — `ApplicationUser` no implementa `IEntity` y
su ciclo de vida es de Identity, que es quien sabe de hashes y bloqueos.

🔴 **Lo importante no fueron los endpoints, sino lo que aparecieron al escribirlos:
bloquear una cuenta NO SERVÍA DE NADA.** Identity comprueba el bloqueo en el *login*, pero
el usuario seguía dentro con su access token y —lo grave— **podía seguir renovándolo
indefinidamente**, porque renovar no vuelve a pedir credenciales y por tanto no pasaba por
el bloqueo. Una cuenta «bloqueada» con la sesión abierta se quedaba dentro **para siempre**.

Cerrado por los dos lados, para que no dependa de acordarse: `LockAsync` revoca todas las
sesiones del usuario, y `RotateAsync` comprueba el bloqueo antes de renovar —esto último
cubre además al usuario que se bloquea solo por fallar el login, donde nadie llama a
`LockAsync`—.

**Reglas duras**, que son las que un descuido rompe sin que nada más falle: un admin no
puede quitarse su propio rol (409, y verificado que sigue siéndolo), no puede bloquearse a
sí mismo —no estaba en el plan y es la más fácil de olvidar—, un rol inexistente se rechaza
con 400 (Identity lo crearía al vuelo, y tendríamos roles fantasma que no protege ningún
`[Authorize]`), y toda promoción se audita con quién, a quién y cuándo.

✏️ **Y una regla que resulta ser inalcanzable**: «no se puede quitar el rol al último
administrador» no se puede provocar por HTTP, porque la regla de «no puedes quitarte el
tuyo» la subsume — o el objetivo soy yo, o hay al menos dos admins. Se deja como defensa
para un futuro endpoint de borrado, pero **queda dicho** en vez de darla por probada.

**202 tests** en verde (eran 193).

### 2026-09-06 — Sesiones que se pueden revocar

Hasta hoy un access token robado valía **60 minutos** y no había forma de invalidarlo: ni
cerrando sesión, ni cambiando la contraseña. `planning/13`, con el diseño fijado por el
owner: **el refresh token viaja en cookie `HttpOnly`**.

- **Rotación con detección de reuso.** Cada refresco gasta el token y entrega otro; que
  reaparezca uno ya gastado solo tiene dos explicaciones, y la rotación existe para que
  sean distinguibles. Fuera de la ventana de gracia se revoca **la familia entera** —con
  dos copias circulando no se sabe cuál es la del dueño—.
- ⚠️ **La ventana de gracia no es un parche: sin ella la detección es inutilizable.** Un
  móvil o una SPA lanzan peticiones en paralelo; dos reciben 401 casi a la vez, las dos
  refrescan con el mismo token y la segunda parece un ladrón. Se cerraría la sesión de
  usuarios legítimos sin parar.
- **`TryConsumeAsync` es un `UPDATE … WHERE RevokedAt IS NULL`**, no un leer-y-escribir:
  dos peticiones simultáneas no pueden gastar el mismo token. Misma forma que el descuento
  de stock.
- **La misma división de `planning/17`**: la *garantía* («esta sesión no se puede
  extender») es la familia revocada en la BASE; la *optimización* es la denylist de `jti`
  en Redis, que solo adelanta la muerte del access token que el cliente ya tiene.
  Verificado con Redis muerto: el logout **sigue cortando la sesión**.

⚠️ **Dos trampas de cookies que habrían fallado en silencio**, las dos con la misma forma
—login 200, cookie no guardada, refresh fallando siempre sin un error en el servidor—:

1. El prefijo **`__Host-`** que proponía el plan exige `Path=/` y `Secure`, y aquí el
   `Path` va acotado y en local se sirve por HTTP. Se descartó.
2. `Secure` decidido por `IsDevelopment()`: el host de tests usa `"Testing"` y va por HTTP,
   así que marcaba la cookie como `Secure` y **ningún test de sesión podía pasar**. Lo
   cazaron los tests. Ahora se decide por `Request.IsHttps`, que se ajusta solo.

Y faltaba **`AllowCredentials()`** en CORS: sin eso el navegador no manda la cookie a otro
origen. Es legal porque los orígenes son una lista explícita — combinarlo con
`AllowAnyOrigin()` está prohibido por la especificación.

`Jwt:ExpirationMinutes` baja de 60 a **15**: con refresh, la ventana en la que un access
token robado sirve es lo único que no se puede cerrar a voluntad.

✏️ Una comprobación mía dio `HttpOnly=False` y era **un falso negativo** (Kestrel emite
`httponly` en minúscula y yo comparaba sensible a mayúsculas). Misma clase de error que el
`grep "[ERR]"` de ayer; el test de la suite ya compara sin distinguir.

**193 tests** en verde (eran 186).

### 2026-09-06 — Los «errores» que no eran errores

Lo último que quedaba abierto de `planning/17` §17.4, y sale de las pruebas de carga: el
log se llenaba de incidentes falsos justo cuando más falta hace leerlo.

- **Un cliente que cuelga no es un fallo del servidor.** EF cancela el `SqlCommand` y
  SqlClient lanza un **`SqlException`** —no una `OperationCanceledException`, que sí estaba
  mapeada— así que salía como 500 con traza completa. `ClientAbortMiddleware` lo absorbe:
  **499**, Information, sin traza.
  ⚠️ Va **por debajo** de `UseExceptionHandler`, y el orden es lo único que lo hace
  funcionar: el middleware de diagnóstico del framework escribe su «unhandled exception» a
  nivel Error **antes** de llamar a ningún `IExceptionHandler`. Lo probé primero en el
  handler y no servía.
- Se decide por el **estado de la petición**, no por el tipo de la excepción: la
  cancelación se propaga distinto según dónde pille, y perseguir tipos es una lista que
  nunca está completa. (`SqlException` ni siquiera se puede construir en un test.)
- **Un timeout de base tampoco es un 500**: `SqlException` −2 pasa a **503 + `Retry-After`**,
  porque es reintentable y un 500 le dice al cliente justo lo contrario.
- **El `Detail` de un 5xx mapeado deja de censurarse**: la regla «≥500 → mensaje genérico»
  estaba pensada para excepciones no controladas. El corte pasa a ser «¿lo mapeamos
  nosotros?».

Medido ejecutando (180 abortos a mitad, con carga de fondo para que la cancelación pillara
a SqlClient en vuelo): **de 27 «unhandled exception» a 0**, y 30 registradas como cierre de
cliente con respuesta 499.

✏️ **Dos correcciones propias.** Las primeras lecturas usaron `grep "\[ERR\]"`, que **no
puede casar** —Serilog escribe `[18:55:27 ERR]`— así que los «0 errores» que di por buenos
eran un falso negativo; rehecho con el patrón correcto. Y al afinar el `Detail` rompí el de
los 4xx, donde el mensaje de dominio es el útil: **lo cazó un test que ya existía**.

Queda dicho lo que no se cierra: EF Core sigue registrando a nivel Error sus 7 líneas de
«An error occurred using the connection», y **se dejan a propósito** —bajar esa categoría
escondería las caídas reales de base—. Y el timeout no se verificó ejecutando: exigía
saturar el SQL Server compartido.

**186 tests** en verde (eran 183).

### 2026-09-06 — La deuda de mensajería, y por qué no se podía probar

Cerrado `planning/12` §12.5 entero. **El patrón de casi todos los puntos era el mismo**: lo
que no se podía probar era lo que vivía dentro de un `BackgroundService` atado a AMQP.

- 🔴 **El test del P0 del consumidor**, que llevaba pendiente desde que se arregló el bug.
  El efecto sale a `IProductPurchasedHandler` y la unidad transaccional a `IMessageInbox`
  —el gemelo de `IEventOutbox`, mismo patrón que `ICommandLog` para HTTP—, así que probarlo
  **ya no necesita broker**. 4 tests, incluido el que de verdad importa: efecto que falla →
  no queda marca → el reintento **reejecuta**. Y uno que fija que la perdedora de una
  carrera es reconocible como choque de PK, porque de eso depende el consumidor para hacer
  ack en vez de gastar un reintento.
- **El contador de intentos pasa a una cabecera nuestra** (`x-retry-attempt`), extraído a
  `RetryAttempts`, una función pura con 6 tests. Arregla dos cosas: un replay desde la DLQ
  ya no vuelve con el presupuesto agotado (`x-death` sobrevivía al paso por la DLQ, así que
  la herramienta para recuperar mensajes no los recuperaba), y se acaba el parseo frágil
  —los valores de texto viajan como `byte[]` y compararlos sin convertir da `false` en
  silencio, error ya cometido una vez—.
- **`RetryDelaySeconds` deja de ser inmutable**: el nombre de la cola de espera lleva su TTL
  dentro, así que cambiarlo declara una cola nueva en vez de dar 406 y dejar la mensajería
  abajo. Verificado: con la de 7 s ya declarada, arrancar a 12 s no da error.
- 🔴 **Y ese cambio abrió un defecto que solo se vio EJECUTANDO**: con las colas de espera
  ligadas a un exchange, cada reintento se copiaba a **todas** —incluidas las de plazos
  anteriores, que siguen existiendo—. Medido: un reintento apareció a la vez en las tres.
  El inbox lo deduplicaba, así que no se ejecutaba de más, pero multiplicaba el tráfico.
  Arreglado publicando al exchange por defecto con el nombre de la cola como routing key;
  `RetryExchange` desaparece, porque nunca aportó enrutado.
- **Canal AMQP reutilizado** en el publicador: medido, 30 eventos abriendo **1** canal en
  vez de 30. Con el acceso serializado, porque los `IChannel` no son thread-safe.
- **`global.json`** fija la banda del SDK y la CI instala los dos: el 10 para compilar
  —igual que aquí, para que «cero warnings» signifique lo mismo— y el 9 porque el SDK 10 no
  trae su runtime y sin él `dotnet test` no puede ejecutar.
- **El orden del outbox pasa a no-goal deliberado**, documentado donde lo va a leer quien
  toque eso (el XML doc de `Sequence`) y con la señal concreta para reabrirlo.

⚠️ **Lo que NO se verificó de punta a punta**, y conviene que quede dicho: el ciclo completo
de reintentos con un efecto que falla de verdad. Los fallos de contenido van a la DLQ **por
diseño**, así que el único disparador es un fallo de infraestructura y provocarlo exigía
tumbar SQL Server, que es compartido. Se cubre en dos mitades (los 4 tests del inbox y los
6 del contador), no de una.

**183 tests** en verde (eran 171), build sin warnings.

### 2026-09-06 — La idempotencia deja de ser una optimización

`planning/16` dejó una pregunta abierta: ¿fallar en cerrado con 503 cuando Redis está vivo
pero lento? **Estaba mal planteada.** Se resolvió con cuatro frentes en paralelo —un
abogado por cada postura, investigación de qué hace la industria, y una lectura DDD/Clean—
y los cuatro llevaron al mismo sitio.

**Lo que decidió el asunto:**

- Los **dos abogados** llegaron por su cuenta a la misma conclusión: el 503 sobre Redis es
  una media medida que compra riesgo de disponibilidad sin comprar corrección.
- La **investigación** encontró que **nadie ejecuta sin garantía**: Adyen devuelve 503 con
  `transient-error: true` y deja que sea *el cliente* quien renuncie omitiendo la cabecera;
  AWS Powertools falla cerrado por construcción. Y Azure documenta el patrón bueno —
  marcador y efectos **en la misma transacción**, con una restricción de unicidad como
  árbitro—, que es donde Stripe guarda sus claves: su misma base de negocio, no una cache.
- El **análisis DDD** señaló que no había que mover la idempotencia entera sino
  **partirla**: el protocolo es adaptador, «este intento no se ejecuta dos veces» es
  invariante de negocio. Y que un `catch` de un adaptador no puede relajar una invariante.
- Y el repo **ya lo hacía bien** en `ProductPurchasedConsumer`: marca y efecto en una
  transacción, arbitrados por una clave primaria. La misma pregunta tenía dos respuestas
  distintas, y la débil estaba en el camino que toca el dinero.

**Lo hecho**: `ExecutedCommands` (PK = la intención) escrita dentro de la transacción del
servicio vía `ICommandLog`, gemelo de `IEventOutbox`; `BuyAsync` recibe una
`CommandIntent` **obligatoria** —renunciar hay que escribirlo— y devuelve un
`CommandOutcome<T>` que reporta si ya estaba ejecutado, que el controller traduce a la
cabecera; el 422 pasa a ser una excepción de dominio; y `[Idempotent]` se queda en **puerta
de admisión**: ya no memoriza respuestas, solo frena duplicados en vuelo con 409 para que
no se apilen bloqueados sobre la misma fila.

**Verificado ejecutando, y esto es lo que importa**: con Redis apuntando a un puerto
muerto, 40 peticiones simultáneas con la misma clave descuentan **1** (3/3) y **todas
reciben 200**; el reintento secuencial descuenta 3 y no 6; el reuso con otro cuerpo sigue
dando 422. Antes eso era **imposible por diseño**, porque el almacén *era* Redis. Con
Redis arriba: ráfagas de 350 y dos réplicas, descuento 1 en 4/4, y 2,00 viajes a Redis.

⭐ **La identidad byte a byte del replay se arregló sola** (deuda desde `planning/12`
§12.2). Al memorizar el DTO en vez de la respuesta HTTP, el replay vuelve a pasar por el
mismo formateador de MVC: el `+` del base64 ya no sale como `\u002B`. El test pasó de
comparar JSON parseado a comparar bytes.

`rules.md` §8 reescrita: la idempotencia **no** era una optimización, y meterla en la misma
frase que la cache costó el agujero medido en `planning/16`. La regla ahora distingue lo
que tiene fuente de verdad alternativa de lo que no.

171 tests en verde, build sin warnings, migración revisada antes de aplicar
(`CREATE TABLE` limpio, sin recrear nada).

### 2026-09-06 — La idempotencia, validada con carga de verdad

Fase de revisión sobre `Idempotency-Key`, contra SQL Server, Redis 7.0.15 y RabbitMQ
reales (la IP del host cambió a `192.168.3.82`). Ejecutando, no compilando.

**Lo que aguantó** — y conviene que quede escrito, porque es la mitad del trabajo:

- **Exactamente-una-vez** con ráfagas simultáneas de 60, 150, 250 y 350 peticiones con la
  misma clave sobre un SKU aislado: el stock baja **1**. 4/4.
- **Exactamente-una-vez entre DOS RÉPLICAS** (dos procesos contra el mismo Redis y la
  misma base, 80 simultáneas alternando instancia). Es la razón por la que el store vive
  en Redis y no en memoria, y **no se había comprobado nunca**.
- Contrato completo: replay, 422 por cuerpo distinto, aislamiento entre usuarios, el
  error no se memoriza, clave acotada por ruta.

**El hallazgo 🔴 — la garantía se apaga sola bajo carga.** `SyncTimeout`/`AsyncTimeout`
están en 1000 ms, una decisión buena tomada para el caso «Redis caído». Pero con Redis
**vivo y sano** basta una ráfaga: un solo multiplexer, 3 operaciones por petición, el
`SET NX` agota el plazo, el store degrada en abierto y **la petición se ejecuta sin
garantía**. Es el caso patológico exacto: la API va lenta → el cliente reintenta → el
reintento entra en la ventana saturada, y la protección contra el doble cobro está
apagada justo entonces.

⚠️ **No se consiguió provocar una duplicación real** en ~20 tandas: hace falta que el
timeout caiga sobre el duplicado, y los duplicados son una fracción minúscula del
tráfico. Se reporta como lo que es —mecanismo demostrado y contado, probabilidad baja,
impacto alto— y no como un bug reproducido.

**Lo corregido:**

- **`SET clave valor EX ttl NX GET`** (Redis ≥ 7.0): reservar y leer en **un** viaje.
  Medido **3,00 → 2,00 viajes por petición**. Y de paso cierra una carrera: el camino que
  reproducía una respuesta recién terminada llamaba a `Replay` **sin comparar la huella
  del cuerpo** — la misma falla silenciosa que el 422 vino a cerrar, alcanzable por
  carrera. Ahora el estado existente sólo puede llegar por un sitio.
- **Token de propiedad en la reserva.** Lo encontró la revisión adversarial y es el
  agujero más feo: `Release` y `Save` eran incondicionales, así que una petición cuya
  reserva ya había caducado podía **borrar la reserva viva de otra**. La ventana de
  duplicación dejaba de estar acotada por el TTL y se reabría en cada vuelta. Ahora
  `Release`/`Save` son un CAS por script Lua y no tocan lo que ya no es suyo.
- **`IdempotencyOptions`** (`ResponseTtlHours`, `ReservationTtlSeconds`, `MaxKeyLength`).
  Los plazos eran `static readonly`: además de saltarse la regla §5, hacían que la
  caducidad de la reserva **no se pudiera probar**.
- **Límite de longitud de la clave** → 400. Se aceptaban claves de 7000 caracteres.
- **El fail-open deja rastro**: `Unavailable` se distingue de `Acquired` (antes eran el
  mismo `true`), sale `Idempotency-Guaranteed: false` y sube un contador.
- **Retirado el código muerto** que intentaba capturar el `Location` para el replay: un
  filtro de acción recupera el control **antes** de que se ejecute el `IActionResult`, así
  que esa cabecera aún no existe. No afectaba a nadie —el único endpoint idempotente
  devuelve 200— pero la documentación lo daba por resuelto.

⚠️ **Lo que NO quedó cerrado, y es lo que importa**: repetida la carga tras el cambio
(14 400 peticiones sobre dos réplicas), el acquire **sigue degradando**: 174 (1,21 %) sin
garantía. Bajar los viajes mueve el umbral, no elimina el modo de fallo. Las salidas
—fallar en cerrado con 503 distinguiendo por `IsConnected`, un multiplexer propio, o más
de una conexión— son decisión del owner, porque `rules.md` §8 exige invertir el fail-open
explícitamente y en todas las implementaciones a la vez.

✏️ **Afirmación retirada**: una primera medición con 200 hilos de `urllib` dio «13× menos
throughput con `Idempotency-Key`». Era el **arnés**, no la API: con un cliente asyncio
sobre sockets crudos el filtro no se mide por encima del ruido, y el pico de 20 s aparecía
también en la columna *sin* clave. Se rehízo la medición antes de apuntar el número.

**169 tests** en verde (eran 160), build sin warnings. Los 9 nuevos incluyen el primer
test que ataca el store directamente, que es donde vive la semántica del token.

### 2026-09-06 — Segunda revisión multiagente: 1 P0 y 4 P1 reales, corregidos

Tres agentes con la orden de **verificar ejecutando**, uno por eje. Encontraron lo que ni
el build limpio ni los 159 tests veían — y el P0 **lo había introducido el trabajo de esa
misma sesión**:

- 🔴 **La marca de idempotencia se confirmaba ANTES del efecto**, así que si el efecto
  fallaba, la reentrega se reconocía como duplicado, se hacía ack y **el mensaje
  desaparecía sin procesarse**: toda la maquinaria de reintentos recién construida era
  inerte para el único caso para el que existe. El razonamiento original (la PK "abre la
  puerta" al efecto) era correcto **cuando no había reintentos**; al añadirlos se volvió
  del revés. Ahora marca y efecto van en **una sola transacción**: si el efecto falla se
  deshacen los dos, y si dos réplicas corren a la vez la PK sigue arbitrando.
  ⚠️ Se verificó que no rompe el camino feliz ni la deduplicación, pero **falta el test
  que fuerce un efecto fallido**: hoy `ProcessAsync` solo escribe un log y no es
  inyectable. Anotado en `planning/12` §12.5.
- 🟠 **El reintento publicaba sin *publisher confirms*** y hacía ack: con el binding
  ausente, el mensaje se evaporaba sin un solo log. Medido borrando el binding.
- 🟠 **Fuga de conexiones a RabbitMQ**: si la topología fallaba, la conexión quedaba viva
  para siempre. Medido **22 fallos = 22 conexiones fugadas**, 1:1.
- 🟠 **`/health/ready` devolvía 503 con Redis caído**, sacando de rotación **todas** las
  réplicas por una dependencia opcional. Pasa a `Degraded` (200).
- 🟠 **El `CorrelationId` no salía en ningún log**: la plantilla de fábrica de Serilog no
  renderiza las propiedades del `LogContext`. El commit anterior lo dio por verificado
  habiéndolo comprobado **con una plantilla puesta a mano**: se verificó el `PushProperty`,
  no la salida. Corregido, y corregido también el registro.
- 🟠 **Un `OperationCanceledException` ajeno al apagado se escapaba** de los bucles del
  publicador y del recolector → con `StopHost` por defecto, **tumbaba la API entera**.
- 🟠 **`If-Match` con lista de ETags daba 400** a un cliente conforme al RFC 9110, y
  aceptaba validadores débiles. Ahora se parsea con `EntityTagHeaderValue`.

✏️ **Corrección honesta de una afirmación mía**: se justificó elegir `sp_getapplock` sobre
el claim por filas diciendo que el claim "destruye la garantía de orden que da `Sequence`".
**Esa garantía no existe**: el `IDENTITY` se asigna al `INSERT` y la fila se ve al `COMMIT`,
así que una transacción lenta con secuencia menor puede confirmar después (verificado). La
decisión sigue siendo la buena —por simplicidad y por no gestionar expiraciones— pero el
argumento era falso y está corregido en el código y en los documentos.

También: un endpoint OTLP mal escrito tumbaba el arranque, el índice de pendientes no
incluía `Attempts` (key lookup por cada mensaje zombi, **para siempre**), MARS desactivaba
los savepoints en los tests, y la clave de caché de la CI incluía el código del curso.

160 tests en verde, build sin warnings, y verificado ejecutando que `/health/ready` pasa de
503 a 200 con Redis caído y que el `CorrelationId` ya se renderiza.

### 2026-09-06 — Deuda 12.3: observabilidad y operación. **`planning/12` cerrado**

- **OpenTelemetry**: trazas (ASP.NET Core, HttpClient, SqlClient) y métricas (+ runtime).
  `ParentBasedSampler` para no partir las trazas distribuidas, y las sondas `/health`
  filtradas —se ejecutan cada pocos segundos y ahogarían cualquier traza que importe.
  ⚠️ Se **instrumenta siempre y se exporta solo si hay `OtlpEndpoint`**: la observabilidad
  no puede ser el motivo de que la API no arranque. Y el **texto** de las consultas SQL no
  se captura: llevaría correos, nombres y precios al backend de trazas.
- **`CorrelationIdMiddleware`**: respeta el `X-Correlation-Id` entrante (para que una
  cadena de servicios comparta uno de punta a punta), lo devuelve en la respuesta y lo
  empuja —con el `TraceId`— a todas las líneas de log. Va **arriba del pipeline**: más
  abajo dejaría sin identificar justo los fallos tempranos.
- **`UseHsts()`** fuera de Development. En Development no, porque la cabecera queda
  cacheada para `localhost` y rompe otros proyectos servidos en claro por ese host.
- **Tercera conexión a Redis unificada** (el health check). Con la cadena de conexión la
  sonda abría la suya: podía decir `Healthy` con una conexión sana mientras la que sirve
  el tráfico estaba rota.

Verificado ejecutando: id generado por el servidor, id del cliente **respetado**, ambos en
el log junto al `TraceId`, un id de 500 caracteres **ignorado**, y sin colector OTLP ni un
solo intento de exportación ni un error. 159 tests en verde, build **sin warnings** (que
importa: la CI va con `-warnaserror`).

**`planning/12` queda cerrado** salvo 12.4, que es una decisión de producto del owner
(licencia de AutoMapper).

### 2026-09-06 — Deuda 12.2: `ETag`/`If-Match` y hash del cuerpo en la idempotencia

**`ETag` / `If-Match`.** Cierra el *lost update* entre dos administradores, que
`RowVersion` **por sí sola no podía**: el PATCH relee la fila, así que EF compara contra el
rowversion del otro y todo cuadra. El GET publica el token como `ETag`, el PATCH lo lee de
`If-Match` y `ProductRules` lo compara → **412**. Opcional a propósito. Verificado el
escenario completo: A lee, B edita (204), A guarda con su token viejo → 412 **y el cambio
de B sobrevive**; A relee y ya puede. Token ilegible → 400, no 500.

**Hash del cuerpo.** Reutilizar una `Idempotency-Key` con otro cuerpo reproducía la
respuesta de la primera **en silencio**. Ahora es **422**. Verificado: compra de 1 → 200,
misma clave mismo cuerpo → 200 (replay), misma clave con `quantity: 5` → 422 y **el stock
solo bajó una vez**.

🟠 **Y destapó un fallo intermitente que llevaba ahí desde el principio**: el replay
re-serializaba el cuerpo con sus propias opciones, así que no era idéntico byte a byte al
de MVC (`+` sale como `\u002B`). Solo se nota cuando el base64 del `rowVersion` lleva un
`+` — o sea, de forma aleatoria. Se pasó a las opciones de MVC y el test compara ahora el
JSON parseado, con la limitación anotada en `planning/12`.

159 tests (6 nuevos: los cinco de `ETag`/`If-Match` y el del 422).

### 2026-09-06 — Deuda 12.1: reintentos del consumidor con contador REAL

`args.Redelivered` es una bandera del broker, no un contador: eran 2 intentos como mucho y
con 0 ms entre ellos, porque un `requeue` devuelve el mensaje a la **cabeza** de la cola.
Ahora hay una cola de espera (`…product-purchased.retry`) con `x-message-ttl` que
dead-letterea de vuelta a la principal, y el contador sale de `x-death[].count`.

⚠️ Dos decisiones que no son obvias:
- Se añade como topología **nueva** en vez de cambiar el `x-dead-letter-exchange` de la cola
  principal. Redeclarar una cola existente con argumentos distintos da **406** y cierra el
  canal: habría que borrar la cola en producción —con sus mensajes— para desplegar. Así el
  despliegue es aditivo.
- Se publica al reintento **antes** de confirmar el original. Al revés, morir entremedias
  pierde el mensaje; en este orden solo provoca una reentrega, que el consumidor deduplica.
  Duplicar antes que perder, la misma regla del outbox.

Verificado con un cuerpo ilegible y TTL de 2 s: intentos a los `:34`, `:36` y `:38` —el
espaciado **es** el TTL— y al tercero a la DLQ. Después, con los valores reales, la
topología declara con TTL 30 s y una compra normal sigue funcionando sin un solo error.

### 2026-09-06 — Deuda 12.1: el outbox, robusto para más de una réplica

- **Orden determinista**: columna `Sequence` (`bigint IDENTITY`). Se ordenaba por
  `OccurredAt`, que es `DateTime.Now` del proceso que escribió la fila: con dos réplicas el
  orden dependía del reloj de cada máquina y dos eventos del mismo agregado podían salir
  invertidos. Ahora lo asigna un único árbitro, el servidor SQL, que además desempata las
  filas del mismo milisegundo.
- **Una sola réplica drena a la vez**, con `sp_getapplock` exclusivo
  (`@LockOwner='Transaction'`, timeout 0). ⚠️ Se descartó el claim por filas
  (`LockedUntil` + `UPDATE ... OUTPUT`) **a propósito**: permite drenar en paralelo, y eso
  destruye la garantía de orden que acabamos de ganar, además de obligar a gestionar la
  expiración del claim. Serializar un trabajo de fondo con lote acotado no cuesta nada.
- **Purga** (`OutboxCleaner`): retención configurable, borrado en tandas de 5.000 con
  `ExecuteDeleteAsync`. Se registra **fuera** del `if` del broker: las tablas crecen aunque
  no haya nadie publicando, y con RabbitMQ apagado crecen más.
- **`OutboxOptions` propio** (sección `Outbox`): `PublishIntervalSeconds`, `BatchSize` y
  `MaxPublishAttempts` salen de `RabbitMqOptions`. El outbox es **agnóstico al broker**;
  tener sus mandos bajo `RabbitMq:` daba a entender lo contrario.

Verificado ejecutando, no solo compilando:
- 3 compras seguidas → publicadas en orden de `Sequence` y consumidas en orde.
- **Dos réplicas reales** (dos procesos contra la misma base): 15 eventos, 7 publicados por
  una y 8 por la otra, **cero duplicados**.
- Contención **determinista**: reteniendo el `sp_getapplock` desde otra sesión SQL, la
  compra sigue respondiendo **200** y no se publica nada; al soltarlo, se publica.
- Purga: borra la fila procesada de 30 días y **conserva** la pendiente con reintentos
  agotados, que es la propiedad que de verdad importa.
- Migración revisada antes de aplicar: `ALTER TABLE ADD [Sequence] bigint NOT NULL IDENTITY`,
  sin recrear la tabla ni perder datos. 153 tests en verde.

### 2026-09-06 — CI (fase 6) y los secretos fuera del repo

**CI** — `.github/workflows/ci.yml`: build + los 153 tests en cada push y PR, con SQL
Server y Redis como `services` del runner (no Testcontainers: mismo camino de código que en
local, sin Docker-in-Docker). Lleva `-warnaserror` —la regla de "0 warnings" dura
exactamente hasta el primer warning que nadie mire— y un job que **construye el
`Dockerfile`**, que nunca se había construido por no haber Docker en el entorno de trabajo.
Para que el mismo `ApiFactory` sirva aquí y allí, los endpoints salen de `TEST_SQL_HOST` /
`TEST_SQL_PORT` / `TEST_SQL_PASSWORD` / `TEST_REDIS`, con los valores locales por defecto.

**Secretos** — `appsettings.Development.json` estaba commiteado con la clave JWT y la
contraseña de SQL. Los tres valores sensibles pasan a **user-secrets**
(`UserSecretsId` en el `.csproj`); el fichero se queda con la configuración no sensible y
sigue commiteado. `rules.md` §5 y §11 actualizadas, y `README_init.md` lleva los comandos
de puesta en marcha.

Verificado en los dos sentidos: la app arranca leyendo los secretos del almacén (login
`admin` 200, `/health/ready` Healthy, 0 errores), y **sin ellos se niega a arrancar** con
`OptionsValidationException: 'Jwt:SecretKey is required'` — que es lo correcto: una clave
de firma no puede degradar en abierto. Suite completa en verde y build `Release` con
`-warnaserror` limpio.

🔴 **Hallazgo aparte, y más grave que lo anterior**: el remoto de git tiene un **token de
GitHub en texto plano** dentro de `.git/config` (`https://ghp_…@github.com/...`). No lo
toca este commit —`.git/` no se versiona— pero **hay que revocarlo en GitHub**: ver §5.

### 2026-09-06 — Paso 11: fases 3, 4 y 5. **153 tests**, y tres bugs que destaparon

Integración con `WebApplicationFactory` sobre SQL Server y Redis **reales**, concurrencia
con `Task.WhenAll`, y degradación y arranque. Solo falta la fase 6 (CI).

**Sin Testcontainers**, a diferencia del plan: no hay Docker dentro del dev container. Se
usa la infraestructura del host con una base propia (`ApiEcommerceNET8_Tests`, borrada y
migrada en cada corrida) y prefijo propio en Redis (`apiecommerce-tests:`). Testcontainers
queda para la fase 6, donde el runner sí tiene Docker.

**Lo que encontraron, que es para lo que están:**

- 🔴 **Los tests de idempotencia pasaban sin probar nada.** El host arrancaba con
  `NoIdempotencyStore` y `NoCacheService` pese a que la configuración final sí traía Redis.
  ⚠️ La causa es estructural: `AddDistributedCaching` (y `AddMessaging`, y
  `AddHealthProbes`) leen la configuración **eager** para decidir *qué implementación
  registrar*, y eso ocurre mientras corre `Program` — **antes** de que se apliquen los
  callbacks de `ConfigureAppConfiguration`. **`UseSetting` sí entra antes.** Un no-op no
  rompe casi ninguna aserción, así que solo cayeron los dos tests que exigían un replay
  real: el resto daba falsa tranquilidad. Queda `TestHostGuardTests` como red permanente.
- 🟠 **La degradación de Redis era correcta pero inservible.** Medido con Redis
  inalcanzable: GET del catálogo **11 s**, compra con `Idempotency-Key` **34 s** — la
  petición acababa bien, pero a esa latencia el cliente ya cortó y los hilos se acumulan.
  Acotados los timeouts (`ConnectTimeout`/`SyncTimeout`/`AsyncTimeout` a 1 s,
  `ConnectRetry` 1) y **unificados los dos multiplexers**: `AddStackExchangeRedisCache`
  creaba el suyo y se quedaba con los timeouts de fábrica, así que la mitad del sistema
  seguía esperando 5 s. Resultado: **3 s y 7 s**. Cierra de paso una deuda de `planning/12`.
- 🟠 **El P0 del crash-loop seguía vivo en otro atributo.** `[EmailAddress]` sobre
  `SeedOptions.AdminEmail` es tan incondicional como lo era el `[Required]` de
  `AdminPassword`: con el seeding apagado y `Seed__AdminEmail=` vacío, el arranque moría.
  Movido a `.Validate(...)`, igual que su hermano.

Además, cambios de producción para poder testear: los **límites de tasa pasan a
configuración** (`RateLimit:*`) — con ellos fijos la suite se limitaba a sí misma a los
100 requests y devolvía 429 por un motivo ajeno a lo que probaba — y `Program` se declara
`public partial` para que `WebApplicationFactory` lo vea.

⚠️ Y una lección de aislamiento: `DegradationTests` y `StartupTests` levantan su **propio**
host y **pasaban en aislado pero fallaban en la suite completa**, chocando con
`Database 'ApiEcommerceNET8_Tests' already exists`. Van en la misma colección sin
paralelismo que el resto aunque no usen su fixture.

Verificado: suite completa **dos veces seguidas** en verde (153/153, ~18 s) y build limpio.

### 2026-09-06 — Paso 11: fases 1 y 2 completas, 105 tests

Primer proyecto de tests del repo. Cubre toda la lógica que no necesita base ni Redis:
reglas de dominio de Category y Product, `CrudService`, `AuthService`, `LocalFileStorage`,
`CachedCategoryService`, `PagedResult`, los perfiles de AutoMapper y
`GlobalExceptionHandler`.

Decisiones que se apartan del plan, todas deliberadas y anotadas en `planning/11`:
`tests/` dentro del repo (la raíz del repo *es* el proyecto), TFM `net9.0` a mano (la
plantilla del SDK 10 solo ofrece `net10.0`), y **sin FluentAssertions** — desde la v8 exige
licencia comercial y ya arrastramos ese problema con AutoMapper.

⚠️ **`ApiEcommerce.csproj` excluye `tests/**`** igual que `AGENTS/**`: sin eso el glob
implícito del SDK Web compila el proyecto de tests dentro de la API, metiendo xunit y Moq
en la imagen de producción y creando una referencia circular con su propio
`ProjectReference`.

**Verificado por mutación**, que es la única forma de saber si un test sirve: se
reintrodujeron dos bugs reales ya corregidos —quitar el `MapFrom` explícito de `CategoryId`
y volver `FindSqlException` a mirar solo el `InnerException` directo— y la suite cazó
exactamente los cuatro tests que debía, ni uno más.

### 2026-09-06 — El evento de dominio vuelve a su slice (pregunta del owner)

El owner preguntó por qué `ProductPurchased` vivía en `Shared/Messaging/Events/` junto a
`IDomainEvent`, si el repo es de vertical slicing. Tenía razón, y el argumento que zanja la
duda no es la simetría sino la **dirección de dependencias**: con el evento —y sobre todo
con su consumidor— en `Shared/`, era `Shared` quien nombraba tipos de `Catalog`, justo al
revés de la dirección declarada **Web → Features → Shared**.

- `IDomainEvent` se queda en `Shared/Messaging/` (contrato, de ningún dominio) y el fichero
  pasa a llamarse como el único tipo que contiene.
- `ProductPurchased` → `Features/Catalog/Events/`: habla de SKU, stock y producto.
- `ProductPurchasedConsumer` → `Features/Catalog/Messaging/`: quién reacciona a un evento
  del catálogo es asunto del catálogo.
- Costura nueva `AddEventConsumer<T>(configuration)` en `Shared/Messaging`: mantiene en un
  solo sitio la política de "solo si hay broker configurado" y deja que cada slice registre
  los suyos. `AddCatalogFeature` pasa a recibir `IConfiguration`.

Buscando más fugas apareció otra cosa: **7 archivos de `Shared/` tenían `using` a
`Features.Catalog` que no usaba nadie**, dejados por el refactor a vertical slicing. Un
`using` sin usar no da warning, así que parecía que media `Shared/` dependía de `Catalog`.
Eliminados y verificado con el compilador. La única dependencia **real** era el escaneo de
AutoMapper (`typeof(CategoryProfile).Assembly`): se **invirtió**, ahora el ensamblado lo
pasa el composition root. `Shared/Mapping/MappingProfile.cs` conserva los suyos a propósito
—es el fichero legacy comentado— y no se toca.

Verificado ejecutando (no solo compilando, que es donde se esconden estos): arranque con la
validación del contenedor, topología del broker declarada, compra → outbox → publicación →
consumo, y los listados paginados de producto y categoría trayendo el mapeo bien (el cambio
del escaneo de AutoMapper compila igual y se rompería en runtime). 0 errores en el log.

### 2026-09-06 — Runtime .NET 9 instalado y outbox drenado del todo

Cerradas las dos decisiones que quedaban abiertas del día anterior:

- **Runtime**: instalado **ASP.NET Core 9.0.19** *side-by-side* con el 10.0.11
  (`dotnet-install.sh --channel 9.0 --runtime aspnetcore`). El SDK sigue siendo solo el
  10.0.400, que es lo correcto: compila `net9.0` sin problema. Los runtimes conviven y cada
  app carga el de su TFM, así que **lo que apunta a `net10.0` fuera de este proyecto sigue
  usando el 10**, que era la condición del owner. La app arranca ya **sin
  `DOTNET_ROLL_FORWARD`** y se comprobó en `/proc/<pid>/maps` que carga `9.0.19`.
- **Outbox**: republicados los 14 eventos que había enterrado el bug de reintentos
  (`UPDATE … SET Attempts = 0 … WHERE Attempts >= 5`). Los 14 se publicaron y consumieron;
  **29 procesados, 0 pendientes**, y `/health/ready` vuelve a **`Healthy`**. Purgada también
  la DLQ, que solo tenía el mensaje sintético de la prueba del tipo inesperado.

⚠️ La instalación del runtime **no sobrevive a recrear el dev container**; el comando queda
en `memory.md` §2.

### 2026-09-05 — Slice 09 verificado contra RabbitMQ real, y el bug que destapó

El owner levantó `rabbitmq_generic`. Ejercitado por fin el camino completo del broker:

- Los **13 eventos** que llevaban en el outbox desde el 2026-08-30 se drenaron solos al
  arrancar: publicados, consumidos y confirmados (`ack 13`).
- Compra nueva → outbox → publicación → consumo → `ack`, con el aviso de stock bajo.
- **Deduplicación**: el mismo `MessageId` publicado dos veces se procesa una
  (`Duplicate … ignored`) y se confirma igual.
- **DLQ**: un `type` inesperado se rechaza sin reencolar y aparece en la dead-letter queue,
  sin llegar a deserializarse.

🔴 **Bug encontrado y corregido: una caída corta del broker enterraba eventos.**
`Attempts` contaba igual "este mensaje falla" que "el broker está caído", y el publicador
además hacía `break` en el primer fallo. Medido: **25 segundos** de broker caído dejaban el
evento con `Attempts=5`, fuera del filtro `Attempts < MaxAttempts` y por tanto **sin
republicarse nunca, ni al volver el broker**. Menos de lo que tarda en arrancar el propio
contenedor de RabbitMQ (`start_period: 30s`), o sea que **un reinicio rutinario del broker
perdía eventos** — justo lo que el outbox existe para impedir.

Corregido con `BrokerUnavailableException`: el broker caído **no consume intentos** y corta
la tanda sin guardar; solo cuenta el fallo atribuible a un mensaje, y con `continue` en vez
de `break` para que un mensaje envenenado no bloquee la cabecera de la tanda. De paso,
`MaxPublishAttempts` pasa a `RabbitMqOptions`: lo leían el publicador y la sonda
`outbox-backlog` como dos `const` separadas con un comentario pidiendo sincronizarlas.

Medido después del fix: **45 s de caída (9 vueltas) → `Attempts` sigue en 0**, y al volver
el broker el evento se publica y se consume.

⚠️ **Quedan 14 eventos enterrados por el bug anterior** (`Attempts=5`, `LastError =
"RabbitMQ is not available."`), que el fix no revive solo: `/health/ready` sigue en
`Degraded` hasta decidir si se republican o se descartan. Ver §5.

⚠️ **El dev container ya solo tiene .NET 10** (SDK 10.0.400, runtime 10.0.11); el proyecto
es `net9.0`. Compila, pero **no arranca** sin `DOTNET_ROLL_FORWARD=Major`. Toda la
verificación de arriba se hizo así, o sea **sobre el runtime 10, no sobre el 9** que usa el
`Dockerfile` (`aspnet:9.0`). Decisión pendiente en §5.

### 2026-09-05 — Compose de despliegue y bloque del broker (`docker-compose.prod.yml`)

Se separó lo que despliega **esta app** de lo que es **infraestructura compartida**:

- `docker-compose.fragment.yml` queda reducido a lo único que falta en el compose central
  del owner: el bloque `rabbitmq_generic`. Se comprobó contra el fichero real que
  `sqlserver_ecommerce` y `redis_generic` ya existen y ya tienen `healthcheck`, así que la
  advertencia que llevaba sobre eso sobraba.
- **`docker-compose.prod.yml`** (nuevo): declara *solo* la API y se engancha a la red del
  compose central como **externa**. ⚠️ Compose prefija la red con el nombre del proyecto:
  `backend` declarada en `000_infra/` se llama `000_infra_backend` — va parametrizada por
  `INFRA_NETWORK`. Y ⚠️ `depends_on` **no cruza ficheros compose**: el arranque ordenado lo
  da `MigrateAsync` + `EnableRetryOnFailure` + `restart: unless-stopped`, no el compose.
- **`.env.example`** (nuevo, commiteado) con `.env` gitignorado. Las variables obligatorias
  usan `${VAR:?…}`, que aborta el `up` en vez de arrancar con un secreto de ejemplo.

Confirmado que **dentro del dev container no hay Docker**: nada de esto se puede construir
ni levantar desde aquí, lo ejecuta el owner en el host. Es la razón de que el slice 09 siga
en ⚠️. Verificado: `dotnet build` limpio (0 warnings). `notes.md` capítulo 22.

### 2026-08-30 — Revisión multiagente y endurecimiento (`63269ac`)

Tres agentes con `dotnet-best-practices`, uno por eje (concurrencia / mensajería /
infraestructura), con instrucción de **verificar ejecutando**. Encontraron bugs que el
build limpio y el smoke test manual **no veían**:

- 🔴 **La app crasheaba al arrancar en Production.** `[Required]` sobre
  `SeedOptions.AdminPassword` se validaba antes de mirar `Seed:Enabled` → con el seeding
  apagado, `OptionsValidationException` → con `restart: unless-stopped`, crash-loop.
- 🔴 **Nadie aplicaba las migraciones**, y `/health/ready` decía `Healthy` igual
  (`AddDbContextCheck` solo comprueba la conexión, no el esquema).
- 🔴 **El `HEALTHCHECK` del Dockerfile usaba `curl`**, que no existe en la imagen `aspnet`.
- 🔴 **`[Transactional]` podía ejecutar la acción dos veces** (lo encontraron dos agentes
  por separado). → nace `ITransactionRunner`.
- 🟠 CORS: la configuración de .NET fusiona arrays → los orígenes de desarrollo seguían
  permitidos en producción.
- 🟠 `RedisIdempotencyStore` fallaba **en cerrado**: un corte de Redis tras el commit
  devolvía 500 por una compra ya cobrada.
- 🟠 Sin `UseForwardedHeaders`, el rate limiter era un cubo global de 100 req/min.
- 🟠 Paquetes: Serilog 10.x y StackExchange.Redis 3.x metían ~12 paquetes 10.x en una app
  `net9.0`, con riesgo de `MissingMethodException` en runtime.

Corregidos todos los P0 y los P1 baratos. Lo que quedó abierto está en
[`planning/12`](planning/12_deuda-revision-multiagente.md).

### 2026-08-30 — Concurrencia, idempotencia, outbox y Docker (`63269ac`)

Rectificación importante de diseño: el stock se implementó primero con `RowVersion` +
reintentos y **se midió que no servía** (15 compras sobre stock 10 → 5×200 + 5×409: no
sobrevendía, pero rechazaba compras válidas). Se cambió a UPDATE condicional atómico.

### 2026-08-30 — Refactor de DI (`5f3b9d0`)

El archivo de DI de 342 líneas se partió: cada feature registra lo suyo en su carpeta;
`Shared/DependencyInjection/` pasa a composition root puro. Además dos fixes reales:
`EnableRetryOnFailure` ausente (que dejaba `[Transactional]` como no-op) y `ICacheService`
registrado con dos lifetimes distintos según la rama.

### 2026-08-30 — Secciones 8–15 del curso (`34a3b44`)

Auth, CORS, cache, versionado, imágenes, paginación y seeding, traídos a esta arquitectura.
Se trajo la *feature*, no el *código*. **No se trajo Mapster** (sección 15).
Fix previo: `AGENTS/**` se compilaba y el build estaba roto con 52 errores.

---

## 3. Verificación acumulada

Medido contra SQL Server y Redis **reales**:

| Prueba | Resultado |
|---|---|
| 15 compras simultáneas, stock 10 | 10×200, 5×409, **stock 0** |
| 8 POST simultáneos de la misma categoría | 1×201, 7×409, **1 fila** |
| 6 compras concurrentes con la misma `Idempotency-Key` | 1×200, 5×409, **una sola compra** |
| 5 reintentos secuenciales con la misma clave | 4 replays, stock intacto |
| Arranque en `Production` con seeding apagado | 200 (antes: crash-loop) |
| Compras con RabbitMQ caído | 3×200, eventos persistidos y reintentándose |
| `Cache MISS` → `HIT` → invalidación en PATCH | correcto; clave verificada en Redis por RESP |
| Subida de `.txt` renombrado a `.png` | 400 (magic bytes) |
| Ventana de rate limit en `auth` | 429 tras 10/min |
| Backlog de 13 eventos al volver el broker | publicados y consumidos, `ack 13`, DLQ vacía |
| Compra → outbox → publicación → consumo | `ack`, aviso de stock bajo correcto |
| Mismo `MessageId` publicado dos veces | efecto aplicado **una** vez, ambos con `ack` |
| Evento con `type` inesperado | `nack` sin reencolar → **1 mensaje en la DLQ** |
| Broker caído 25 s (**antes del fix**) | evento enterrado con `Attempts=5`, **nunca republicado** |
| Broker caído 45 s (**después del fix**) | `Attempts=0`; al volver el broker, publicado y consumido |
| Republicación de los 14 enterrados | 29 procesados, 0 pendientes, `/health/ready` → `Healthy` |
| Arranque sobre el runtime **9.0.19** | sin `DOTNET_ROLL_FORWARD`; verificado en `/proc/<pid>/maps` |
| **105 tests unitarios** | verdes; y en rojo al reintroducir dos bugs reales (prueba de mutación) |
| **153 tests** (unit + integración) | verdes dos corridas seguidas, ~18 s, contra SQL Server y Redis reales |
| 15 compras simultáneas, stock 10 (**automatizado**) | 10×200, 5×409, stock 0 — idéntico a la medición manual |
| 8 POST simultáneos misma categoría (**automatizado**) | 1×201, 7×409, 1 fila, ni un 500 |
| 6 compras concurrentes misma clave (**automatizado**) | una sola compra |
| GET del catálogo con Redis caído | 11 s **antes** del fix de timeouts → **3 s** después |
| Compra con `Idempotency-Key` y Redis caído | 34 s → **7 s**; y responde 200, no 500 |
| Arranque con seeding apagado y sin admin | 200 (destapó que `[EmailAddress]` lo rompía) |
| Arranque sin `Jwt:SecretKey` | **falla al arrancar**, que es lo correcto |

**No verificado**: el `Dockerfile` construido y `docker-compose.prod.yml` levantado — no hay
Docker en el dev container, los ejecuta el owner en el host.

---

## 4. Pendientes, en orden

Ningún paso del roadmap está abierto salvo el 15. Lo que queda es **dominio, verificación
que necesita credenciales del owner, y deuda menor** (`docs/06` §Paso 12).

1. 🔴 **Cobro real contra Stripe, sin verificar.** Está probado el 503 sin pasarela, el 503
   de pasarela caída y la verificación de firma (firmando a mano con el mismo esquema).
   Falta el camino feliz entregado por Stripe: `sk_test_…` + `whsec_…` en user-secrets y una
   URL pública (`stripe listen --forward-to localhost:8021/api/v1/payment/webhook/stripe`).
2. **CI** ([`planning/11`](planning/11_proyecto-de-tests.md) fase 6) — **escrita y
   desactivada** en `AGENTS/ci/ci.yml.disabled`: nunca ha corrido, porque el PAT del remoto
   no tiene scope `workflow`. Es lo que habría cazado el CS9113 de esta sesión: `-warnaserror`
   solo lo pone la CI, y sin CI la regla de «0 warnings» depende de que alguien mire.
   Mientras no haya remoto por SSH, lo barato es meter `-warnaserror` en el `.csproj`.
3. **Paridad con una tienda real**, pasos 5–6 de [`planning/24`](planning/24_paridad-con-tesloshop.md):
   dirección de envío estructurada (hoy es un `string(500)`), y **PayPal** junto a Stripe,
   que además trae el reembolso y con él poder cancelar una orden pagada.
4. **`Shipping`** — el sexto y último contexto acotado previsto, y lo único grande que falta
   de dominio. De paso le daría consumidor a `order.placed`, que hoy no se emite porque
   publicar sin cola vuelve como 312 NO_ROUTE.
5. **Deuda menor anotada** — `docs/06` §Paso 12. Lo más caro de ahí es `DateTime.Now` → UTC
   (entidades, DTOs y datos, todo a la vez) y que **nadie alerta cuando la DLQ crece**.
   ⚠️ Y una que no es de código: **repasar las licencias antes de añadir un paquete**. Ya han
   mordido tres veces (AutoMapper, FluentAssertions, MassTransit); el estado del stack está
   en `notes.md` cap. 44.
6. **Partir en proyectos** ([`planning/15`](planning/15_partir-en-proyectos.md)) — diferido
   a propósito: hacerlo antes de que el proyecto lo pida solo añade fricción.

---

## 5. Decisiones que esperan al owner

| Tema | Pregunta |
|---|---|
| **Licencia de AutoMapper** | ⚠️ **El dato que había aquí era FALSO** (verificado el 2026-09-12). La 15.1.1 es dual **RPL-1.5 + comercial**, y existe una **licencia Community gratuita** por debajo de **5 M USD** de ingresos brutos y ≤10 M de capital externo, de autoservicio y sin aprobación. Y la última **MIT** es la **14.0.0** (feb 2025), no la 13.x. Así que las opciones son: **registrar la Community** (0 €, una clave), **fijar 14.0.0**, o **migrar a Mapperly** (Apache 2.0, source generator, sin umbral ni clave). ⚠️ Lo que NO conviene es quedarse en 15.x sin registrar: entonces aplica **RPL-1.5, que es copyleft recíproco** |
| **Política de commits** | `rules.md` §12 dice que el agente commitea (práctica de este repo). En el repo de frontend del owner la regla es la contraria. ¿Se confirma? |
| 🔴 **Token de GitHub en `.git/config`** | El remoto es `https://ghp_…@github.com/AlexMartin998/dotnet-ecommerce-v1.git`: un **PAT en texto plano** que aparece en cualquier `git remote -v`. **Revocarlo en GitHub** (Settings → Developer settings → Personal access tokens) y volver a autenticar con `gh auth login` o con SSH. Es lo más urgente del repo. |
| **Secretos ya en el historial** | Resuelto para adelante (user-secrets), pero la clave JWT y la password de SQL **siguen en los commits anteriores**. La JWT ya se rotó al migrar; la de SQL es la del contenedor local compartido. Limpiar el historial (`git filter-repo`) solo compensa si el repo se hace público. |
| **Migrar a `net10.0`** | Resuelto por ahora instalando el runtime 9 (el owner quiso mantener el 10 para lo demás). Sigue abierto a futuro: alinearía el proyecto con el SDK y con `dotnet-ef` 10, hoy desalineados. |
