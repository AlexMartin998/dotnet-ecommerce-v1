# 28 — Categorías destacadas, listados paginados y orden estable de imágenes

> Pedido del front (`ecom_angular`), segundo encargo, en modo autónomo. Contrato:
> `features/28_destacadas-y-paginacion.feature`. Nada de §11.1: una columna nullable nueva, y
> su índice único y su CHECK solo afectan a datos que crea la propia migración.

---

## 0. Las decisiones

### A. Destacadas = las categorías que ya hay (Shirts, Hoodies, Hats), no Hombre/Mujer/Niños

- En este modelo **una categoría es el TIPO de prenda** y un producto tiene **una**. El género
  es otra dimensión: una camiseta es a la vez «Shirts» y «men». Hacer categorías Hombre/Mujer/
  Niños obligaría a elegir entre las dos dimensiones o a pasar a N:M con jerarquía, y eso es
  rehacer el catálogo entero para un menú.
- El header estilo Teslo (Men · Women · Kids) es **una faceta por etiqueta**, que ya existe
  (`tags` lleva el género desde `planning/26`). Si el owner la quiere, es un filtro
  `?tag=men` en los listados paginados, no un cambio de modelo. **No se hace ahora**: no está
  pedido y no conviene decidirlo por el owner.
- **Una columna, no dos**: `Category.FeaturedPosition int?`. `null` = no destacada, 1..3 =
  destacada y su orden. Un booleano + un orden deja estados imposibles (orden sin destacar).
- **La base es el árbitro** (§7.1): `CHECK (FeaturedPosition BETWEEN 1 AND 3)` + índice único
  filtrado. Con eso **no caben más de 3** aunque dos administradores escriban a la vez o un
  servicio se equivoque. El 400 `featured_limit_reached` del servicio solo da el mensaje.
- **Un `PUT` que reemplaza la lista entera**, no marcar/desmarcar una a una: marcar, desmarcar
  y reordenar son la misma operación y va en una transacción. Con operaciones sueltas, reordenar
  son varias escrituras que chocan con el índice único a mitad. Es `PUT` y no `PATCH` porque
  reemplaza el recurso «lista de destacadas» entero (§9 habla de actualizar entidades).

### B. Paginación: `PagedResult` por offset, no cursor

- Mismo contrato que `/product/paged` y `/category/paged`: el front ya lo consume.
- Orden total `CreatedAt DESC, Id DESC` (§9).
- ⚠️ **Lo que el offset no evita**: un producto creado mientras alguien hace scroll desplaza
  las páginas y el siguiente lote **repite** un elemento (nunca se salta ninguno por altas; sí
  por bajas). El front **deduplica por `id`**. Un cursor lo evitaría, pero el catálogo cambia
  poco y tener dos contratos de paginación cuesta más que deduplicar.

### C. Imágenes: `Position`, desempate por `Id`

Ya se ordenaban por `Position`; faltaba el desempate para que dos imágenes con la misma
posición no bailen entre peticiones. La primera es la principal.

## 1. Checklist

- [x] `Category.FeaturedPosition` + CHECK + índice único filtrado. Migración revisada.
- [x] `CategoryDto.FeaturedPosition`; `GET /category/featured` (anónimo, cacheado).
- [x] `PUT /category/featured` `{ categoryIds }` (admin): transacción + `sp_getapplock`,
      400 `featured_limit_reached`, 400 ids repetidos o inexistentes. Invalida la cache.
- [x] Seed: Shirts 1, Hoodies 2, Hats 3. Base local: fijarlas por la API.
- [x] `GET /product/category/slug/{slug}/paged` y `GET /product/search/paged`.
- [x] Búsqueda sin paginar: faltaba el desempate por `Id` en su `OrderBy`.
- [x] Imágenes y variantes: `ThenBy(Id)`.
- [x] Tests, build, suite, OpenAPI.

## 2. Verificación

- [x] Build `-warnaserror` limpio y suite **432/432** (+12: `FeaturedAndPagingTests` ×11, mapper ×1).
- [x] **El applock se probó quitándolo**: 10 `PUT` simultáneos con listas que se pisan dan 409
      (deadlock / índice único) en 3 de 3 ejecuciones; con él, 10 × 200 y quedan 3.
- [x] El CHECK se probó saltándose el servicio: un `ExecuteUpdate` a posición 4 falla con
      `CK_Categories_FeaturedPosition`.
- [x] Migración revisada (columna nullable + índice único filtrado + CHECK) y aplicada al arrancar.
- [x] Base local: las destacadas se fijaron **por la API** (`PUT` → Shirts 1, Hoodies 2, Hats 3),
      no con un UPDATE a mano; una cuarta → 400 `featured_limit_reached`.
- [x] `/product/category/slug/hoodies/paged?pageSize=4` → 7 en 2 páginas; búsqueda `hoodie` → 6.
- [x] OpenAPI regenerado: 50 rutas.

⚠️ **Lo que queda**: la validación de ids existentes va fuera de la transacción; si alguien
borra una categoría justo entre medias, el `PUT` deja una destacada menos, sin error. Borrar una
categoría ya exige que no tenga productos, así que no se ha endurecido.
