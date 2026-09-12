# 24 — Paridad de negocio con una tienda real (TesloShop)

> Trae **contexto de negocio** de un e-commerce Next.js con panel de administración
> (`003_miscellaneous/.../006_teslo-shop`), bajo los patrones de **este** backend.
>
> No trae código ni decisiones de aquel proyecto: trae la *feature*, igual que con el curso.
> Sus bugs conocidos están en su checklist §15 y aquí no deben poder pasar.

---

## 0. El diagnóstico, en una línea

**Las garantías sobran; falta superficie de tienda.** Todo lo que aquel proyecto pone como
*objetivo difícil* —reserva de stock en transacción, idempotencia, estados condicionales,
jobs de limpieza— aquí está hecho y medido. Lo que falta son endpoints y campos de catálogo
que cualquier front de tienda da por sentados.

| Lo que pide el front | Aquí | |
|---|---|---|
| Cotizar el carrito con precios y stock actuales | no existe | 🔴 paso 1 |
| Mover la orden `paid → preparing → shipped → delivered` | estados en el enum, **ningún endpoint los alcanza** | 🔴 paso 2 |
| Contadores para el panel | no existen | 🔴 paso 3 |
| `slug`, varias imágenes, tallas, tags, género | solo `Category` y una `ImageUrl` | 🟠 paso 4 |
| Dirección de envío estructurada | `string(500)` | 🟠 paso 5 |
| PayPal junto a Stripe | solo Stripe | 🟠 paso 6 |
| Carrito en base de datos | no hay | ✅ **no hace falta** (§0.2) |

### 0.1 Lo que este backend ya tiene y aquel ni plantea

Outbox transaccional, inbox, DLQ con reemisión, comprobante en PDF fuera de la petición,
concurrencia optimista con `ETag`/`If-Match`, degradación explícita, rate limiting, sondas,
trazas. **No se toca nada de eso**: lo que entra aquí entra por encima.

### 0.2 Por qué el carrito NO va en base de datos

El propio documento de migración lo pone como fase 2 y como **pregunta abierta** (su §16).
El carrito vive en la cookie del cliente; lo que quita el bug no es la tabla, es **cotizar
en el servidor**. Un carrito en BD solo añade «sígueme entre dispositivos», que es una
decisión de producto, no de arquitectura. Se deja fuera a propósito.

---

## 1. `POST /api/v1/cart/quote` — el hueco número uno

**Dónde**: `Features/Ordering/`. Cotizar es vocabulario del checkout, no del catálogo; lo
que necesita del catálogo entra por el puerto que ya existe (`ICatalogGateway`).

### 1.1 🔴 La trampa que hay que evitar, y es LA razón de este paso

El bug nº4 de TesloShop es **el impuesto calculado en dos sitios** (0.15 en el cliente, 0.12
en el servidor, comparados con igualdad exacta de floats). Si aquí la cotización calcula sus
totales y `PlaceAsync` calcula los suyos, **se reproduce el mismo bug dentro de este repo**.

- [ ] Nace `OrderPricing` (`Features/Ordering/Service/`): de líneas a desglose, **una sola
      pieza pura y sin dependencias**.
- [ ] `BuildAsync` deja de sumar por su cuenta y la usa.
- [ ] `QuoteAsync` usa la misma.
- [ ] Test que compara **cotización contra orden real** con el mismo carrito: si alguien
      añade el IVA en un solo sitio, cae.

### 1.2 Cotizar NO aparta stock

`ICatalogGateway.TryTakeAsync` **reserva**, y eso es correcto para comprar y veneno para
cotizar: un carrito abandonado dejaría el catálogo a cero.

- [ ] Método nuevo en el puerto: `PeekAsync(sku)` → `QuotableItem?(ProductId, Sku, Name, UnitPrice, Stock)`. Solo lectura.
- [ ] El adaptador lo resuelve con `GetBySkuAsync`, que ya existe.

### 1.3 Una línea sin stock devuelve **200**, no 409

Es una diferencia deliberada con `POST /order`, y la razón es el caso de uso: el front tiene
que **enseñar** «solo quedan 2» y dejar ajustar la cantidad. Un 409 deja el carrito inservible.

- [ ] Cada línea lleva `status`: `ok` · `insufficient_stock` · `not_found`.
- [ ] `available` y `maxQuantity` por línea, para que el front no adivine.
- [ ] El total **solo suma las líneas `ok`**, y el cuerpo lleva `allAvailable: bool`.
- [ ] Los SKU repetidos se agrupan **igual que en `BuildAsync`** (misma regla, o la
      cotización y la compra difieren en el número de líneas).

### 1.4 Contrato

```
POST /api/v1/cart/quote          🔓 anonimo (el carrito existe antes de la sesion)
{ "items": [ { "sku": "MQ-1", "quantity": 2 } ] }        <- NUNCA precios

200 { "items": [ { "sku", "productId", "name", "unitPrice", "quantity",
                   "lineTotal", "available", "maxQuantity", "status" } ],
      "allAvailable": true,
      "currency": "USD", "subtotal", "discount", "tax", "shipping", "total" }
422  el cuerpo no valida (cantidad <= 0, mas de 50 lineas)
```

⚠️ **Una cotización es una foto, no una promesa.** Entre cotizar y comprar, el precio y el
stock pueden cambiar: manda el checkout. Va escrito en la respuesta y en el `<remarks>`.

---

## 2. `PATCH /api/v1/order/{id}/status` — los tres estados muertos

`OrderStatus` tiene `Preparing(2)`, `Shipped(3)` y `Delivered(4)` y **ningún camino llega a
ellos**. Es el mismo defecto que `ReceiptStatus.Failed` antes de `OnExhaustedAsync`: un
estado inalcanzable es un enum que miente.

- [ ] `PATCH /api/v1/order/{id}/status` 👑, cuerpo `{ "status": "preparing" }`.
- [ ] Transiciones legales, y **solo** estas:

```
paid  ->  preparing  ->  shipped  ->  delivered      (delivered es final)
```

- [ ] **UPDATE condicional** (`WHERE Id = @id AND Status = @esperado`), como el webhook.
      0 filas → releer: si ya está en el destino es un **reenvío → 204**; si no, **409
      `invalid_transition`**.
- [ ] ⚠️ **Sin `Idempotency-Key` y sin `CommandIntent`, a propósito.** La idempotencia aquí
      la da el propio UPDATE condicional, no el registro de comandos: no hay nada que
      «ejecutar dos veces», solo una fila que ya está donde se la quería dejar.
- [ ] ⚠️ **No emite ningún evento.** Nadie consume `order.shipped`, y publicar sin cola que
      lo acepte vuelve como **312 NO_ROUTE** y agota el outbox en silencio. Entrará el día
      que `Shipping` o las notificaciones lo escuchen.
- [ ] `ExecuteUpdateAsync` no dispara la auditoría: `UpdatedAt` se pone a mano, con la fecha
      capturada en una variable local (dentro del árbol de expresión se traduce a `GETDATE()`).

### 2.1 Lo que este endpoint NO hace, y por qué

**Cancelar y reembolsar quedan fuera.** Cancelar una orden pagada exige devolver el dinero, y
el reembolso no existe todavía (ni `PaymentStatus.Refunded` ni `OrderStatus.Refunded`).
Cancelar una `placed` ya tiene dueño: `AbandonedOrderCleaner`, que devuelve el stock **en la
misma transacción que la transición**. Es la deuda que `docs/06` §Paso 12 ya registra:
«devolver stock tiene sus propias invariantes; no se improvisa junto a esto». Paso 6.

---

## 3. Contadores del panel — uno por contexto, no un `/admin/dashboard`

⚠️ **Decisión que se aparta del documento de origen.** Aquel define `GET /admin/dashboard`
con conteos de órdenes, clientes y productos. Aquí eso obligaría a un slice a conocer a otros
tres, o a inventar un sexto contexto cuyo único dominio es *una pantalla*.

Cada contexto publica **los suyos**, y el front compone con tres llamadas en paralelo:

| Endpoint | Devuelve |
|---|---|
| `GET /api/v1/order/stats` 👑 | total, y el recuento por estado (placed, paid, preparing, shipped, delivered, cancelled) |
| `GET /api/v1/product/stats` 👑 | total, `outOfStock` (= 0) y `lowStock` (**1..10**) |
| `GET /api/v1/user/stats` 👑 | total, admins, bloqueados |

- [ ] ⚠️ `lowStock` **excluye el 0**: mezclarlos hace que el panel pida reponer lo que ya no
      se puede vender (es el 🩹 que el documento de origen marca en su dashboard).
- [ ] Se cuentan con `CountAsync` en la base, nunca trayendo filas.

---

## 4. Catálogo de tienda (siguiente)

`slug` único, `product_images` (varias por producto), `sizes`, `tags`, `gender`. Es **una
migración** y toca el slice de referencia, así que va aparte y con su propio planning.

⚠️ Hallazgo de la comparación: **`OrderItems` no tiene FK a `Products`** (solo a `Orders`).
Borrar un producto vendido funciona en silencio y el `ProductId` de la línea queda colgando.
La línea congela sku/nombre/precio, así que la orden sobrevive, pero el front no puede
enlazar «volver a comprar». El borrado lógico entra aquí.

---

## 5. Dirección de envío estructurada (siguiente)

`ShippingAddress string(500)` → objeto con `firstName, lastName, line1, line2, city, zipCode,
country, phone`. Alguien está concatenando y nadie puede volver a partirlo. Mejor migrarlo
antes de tener órdenes reales dentro.

---

## 6. PayPal junto a Stripe (siguiente)

El registry ya resuelve por petición: añadir un proveedor es **una clase y un
`AddSingleton`**. Pero hay un detalle que solo se ve mirando el puerto:

```
Stripe:  la API crea el intento  ->  el cliente confirma  ->  webhook                (2 pasos de servidor)
PayPal:  la API crea la orden    ->  el cliente APRUEBA   ->  la API CAPTURA  ->  webhook
                                                              ^^^^^^^^^^^^^^
                                                              no cabe en IPaymentGateway
```

`IPaymentGateway` tiene hoy `CreateIntentAsync` y `ParseEvent`: está hecho a la medida del
modelo de Stripe. Añadir PayPal sin tocar Stripe es un `CaptureAsync` con **implementación
por defecto** en la interfaz (default interface member, como en `IEntityRules`) que lance
`NotSupported`, más `POST /payment/{id}/capture`.

Y con PayPal entra el reembolso (`Refunded` en los dos enums), que es lo que desbloquea el
paso 2.1.

---

## 7. Verificación (§11 de `rules.md`)

- [ ] `dotnet build -warnaserror` limpio y suite en verde.
- [ ] **Ejecutando**, no compilando:
  - [ ] cotizar 5 unidades de un producto con stock 5 → el stock **sigue en 5**;
  - [ ] cotizar y comprar el mismo carrito → **el mismo total**;
  - [ ] una línea sin stock → 200 con la línea marcada y el total sin ella;
  - [ ] `paid → preparing → shipped → delivered`, y repetir el último → **204**;
  - [ ] `placed → preparing` → **409 `invalid_transition`**;
  - [ ] transición **simultánea** por dos administradores (`for … & done; wait`): una 204,
        la otra 204 por reenvío, y **una sola** escritura;
  - [ ] los tres `/stats` con un usuario sin rol → **403**.
