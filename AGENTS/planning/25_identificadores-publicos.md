# 25 — Identificadores públicos no enumerables

> Pedido por el front Angular (`02_front/01_ecommerce_csharp_front`, su
> `AGENTS/context/api-contract-01-front-gaps.md` §10). El owner dio vía libre para decidir
> el diseño («vamos, sé autónomo», 2026-09-13), y eso incluye las operaciones de §11.1 que
> hacen falta aquí, sobre la base local de desarrollo.

---

## 0. La decisión

| Recurso | En la ruta | Por qué |
|---|---|---|
| **Order** | `publicId` (UUID v7) | Es de un comprador: con el entero se enumeran órdenes y se mide el volumen |
| **Payment** | `publicId` (UUID v7) | Igual |
| **Category** | `slug` para leer; `id` para las escrituras de admin | Mismo criterio que `Product`, que ya tenía slug |
| **Product** | sin cambios (`slug` para leer, `id` en admin) | El catálogo es público: su `id` no filtra nada que el listado no enseñe |
| **User** | sin cambios | Ya es un GUID de Identity |

- **UUID v7 y no v4**: se ordena por tiempo, así que el índice único no se fragmenta con cada
  INSERT. Filtra el milisegundo de creación, que la orden ya expone en `placedAt`; lo que no
  filtra es el volumen (74 bits aleatorios).
- **La clave primaria sigue siendo la PK**: las FK, los eventos (`OrderPaid.OrderId`) y los
  UPDATE condicionales internos siguen en `int`. Lo público es una columna aparte con
  índice único, no un cambio de PK, que obligaría a reescribir todas las FK.
- ⚠️ **Lo que NO se arregla, a propósito**: `number` (`ORD-2026-000071`) y `reference`
  (`PAY-2026-…`) siguen siendo secuenciales. Son lo que cita el cliente y lo que se imprime
  en el comprobante. Siguen dejando **estimar el volumen**, pero ya no **enumerar**: no son
  ruta de nada. Queda anotado como decisión abierta del owner en `memory.md`.
- **Se rompe el contrato v1 sin sacar una v2**: no hay ningún cliente en producción, y el
  único consumidor (el front) es quien lo pide.

## 1. Categorías

- [x] `Category.Slug` (`MaxLength(60)`, índice único), derivado con `Slugs.From` al crear.
      **No se actualiza** en el PATCH.
- [x] `CategoryRules`: 409 si el slug derivado ya existe (solo pasa con nombres que difieren
      en espacios: el nombre ya es único y solo admite letras, dígitos y espacios).
- [x] `CategoryDto.Slug`, `ProductDto.CategorySlug`.
- [x] `GET /category/slug/{slug}` y `GET /product/category/slug/{slug}` (404 si la categoría
      no existe, igual que la ruta por id).
- [x] Aprovechando: `GetProductsForCategoryAsync` ordenaba sin desempate por PK (CLAUDE.md §9).

## 2. Órdenes

- [x] `Order.PublicId` (`Guid`, índice único), `Guid.CreateVersion7()` al construirla.
- [x] `OrderDto`: `publicId` sustituye a `id`.
- [x] Rutas `GET /order/{publicId:guid}`, `/{publicId:guid}/receipt`,
      `PATCH /{publicId:guid}/status`; `CreatedAtRoute` con `publicId`.
- [x] Repositorio: `FindForBuyerAsync`, `TryTransitionAsync` y `FindStatusAsync` por
      `publicId` para lo que llega de fuera; las transiciones internas (`TryMarkPaid`,
      `TryCancel`) siguen por id.

## 3. Pagos

- [x] `Payment.PublicId` y `Payment.OrderPublicId` (copiado de la orden, como `OrderNumber`).
- [x] `StartPaymentDto.OrderPublicId` (`Guid?` con `[Required]`: sin él es 400, no un 404
      contra `Guid.Empty`).
- [x] `PaymentDto`: `publicId` y `orderPublicId` sustituyen a `id` y `orderId`.
- [x] `GET /payment/{publicId:guid}`; `IOrderingGateway.FindPayableAsync(Guid, …)`.

## 4. Migración (`PublicIdentifiers`) — §11.1, revisada a mano

Orden: columnas (EF las crea `NOT NULL` con `Guid.Empty`/`''` de relleno) → `UPDATE` que
da a cada fila su valor → índices únicos. Tal como la generó EF, el índice iba antes del relleno
y reventaba contra 96 órdenes con el mismo `Guid.Empty`.

- [x] `Categories.Slug`: relleno en SQL con la misma normalización para los nombres del
      seed (sin tildes: el DTO no admite caracteres especiales). Se comprueba antes que no
      haya colisiones.
- [x] `Orders.PublicId = NEWID()` y `Payments.PublicId = NEWID()` para las filas viejas
      (v4: da igual, no hay nada que ordenar en lo histórico).
- [x] `Payments.OrderPublicId` desde `Orders` por `OrderId`.
- [x] Revisar que EF no proponga ningún drop.
- ⚠️ `ExecutedCommands` guarda el **resultado serializado**: un reintento con una
  `Idempotency-Key` de ANTES de la migración devuelve el JSON viejo (con `id`, sin
  `publicId`). Solo afecta a claves dentro de la retención y a la base de desarrollo; no se
  reescriben esas filas.

## 5. Verificación

- [x] Build `-warnaserror` limpio, suite en verde (tests adaptados + los del `.feature`).
- [x] Ejecutando contra la base real: migración aplicada sobre los datos existentes, recorrido
      orden → pago → estado por `publicId`, y la ruta vieja por entero da 404.
