# 20 — Órdenes y su comprobante en PDF

> Contrato: [`features/20_ordenes-y-comprobante.feature`](../features/20_ordenes-y-comprobante.feature).
>
> Hasta hoy `POST /api/v1/product/buy` descuenta stock y **no deja rastro de qué se
> compró**: no hay historial, no hay documento y no hay nada que enseñarle al cliente.
> Esto añade el contexto acotado `Ordering` —el cuarto de la lista prevista en
> `rules.md` §4.1— y su comprobante imprimible.
>
> Alcance fijado con el owner el 2026-09-06: **orden de un solo vendedor**. La imagen de
> referencia (`__ref__/02/img`) era un marketplace con sub-órdenes por *seller*; aquí no
> hay sellers. Lo que sí se toma de ella es la **forma del documento**: líneas, totales
> desglosados, datos del cliente, dirección de envío y estado.

---

## 20.0 Las tres decisiones que ordenan todo lo demás

Todo lo que sigue sale de tres frases. Si alguna cambia, cambia el diseño entero:

1. **El PDF no se genera dentro de la petición.** Se emite `OrderPlaced` por el outbox que
   ya existe y un consumidor lo genera aparte. Generar un documento tarda, y **una compra
   ya cobrada no puede depender de que el generador esté vivo**.
2. **La base guarda una clave opaca, no una ruta.** Persistir
   `/app/documents/2026/09/x.pdf` ata la base a la infraestructura de hoy: el día que los
   comprobantes vivan en S3 habría que reescribir todas las filas.
3. **Lo que se copia en la orden se congela.** Precio, nombre y SKU quedan como estaban al
   comprar. Un comprobante que cambia cuando cambia el catálogo no es comprobante de nada.

---

## 20.1 El almacén de documentos — cambiar de infra sin tocar el código

Es la petición literal del owner: hoy sistema de ficheros, mañana S3 / R2 / MinIO /
Cloudinary, **cambiando solo infraestructura**.

- [x] `Shared/Documents/IDocumentStore.cs` — puerto: `SaveAsync` / `OpenAsync` /
      `DeleteAsync` por **clave opaca**. Quien llama la guarda y la devuelve; no puede
      construirla, interpretarla ni convertirla en ruta.
- [x] ⚠️ **No se fusiona con `IFileStorage`.** Aquel guarda imágenes de producto **dentro
      de `wwwroot/`** para que `UseStaticFiles` las sirva a cualquiera: son públicas y esa
      es su gracia. Un comprobante lleva nombre, dirección y lo que se pagó. Dos
      necesidades opuestas no caben detrás de la misma abstracción por mucho que las dos
      «guarden ficheros».
- [x] `LocalDocumentStore` — escribe **fuera de `wwwroot/`**, clave
      `aaaa/mm/<32 hex>.pdf` (carpetas por año/mes: un solo directorio con cientos de miles
      de ficheros hace lento hasta un `ls`), parte aleatoria de un CSPRNG.
- [x] ⚠️ **Comprobación de traversal canonicalizada** en `TryResolve`: la clave viene de la
      base, pero basta una fila manipulada o un endpoint futuro que la acepte del cliente.
      Se compara la ruta ya resuelta contra la raíz; filtrar por la cadena `".."` no cubre
      rutas absolutas ni enlaces simbólicos.
- [x] `DocumentStorageOptions` + `AddDocumentStorage(configuration)`: el `switch` por
      proveedor es **el único sitio que se toca** el día del cambio. ⚠️ Un proveedor
      desconocido **tumba el arranque**: caer al disco ante un `"s3"` mal escrito
      significaría escribir comprobantes en un contenedor efímero creyendo que están en el
      bucket, y enterarse en el primer reinicio.
- [x] Registrarlo en `AddSharedInfrastructure` y declarar la sección `Documents` en
      `appsettings.json`.

**Patrón**: no es Strategy ni Factory, es **puerto + adaptador** elegido en el composition
root. Strategy elegiría por petición; aquí la elección es del *despliegue* y se hace una
vez, al construir el grafo de DI — la misma forma que ya usan `AddDistributedCaching` y
`AddMessaging`.

## 20.2 El modelo

- [x] `Order` + `OrderItem` (`Features/Ordering/Models/`). `Order : IAuditable`,
      `OrderItem : IEntity`.
- [x] **Número legible** `ORD-<año>-<secuencia>`, columna real con índice único: la gente
      cita ese número. ⚠️ Sale de una **secuencia de SQL Server**, no de un `MAX()+1`
      —leer-y-escribir, y dos compras simultáneas se llevarían el mismo número contra un
      índice único—.
- [x] ⚠️ **`OrderItem.ProductId` sin clave foránea**, a propósito: si un producto se borra,
      la orden y su comprobante tienen que sobrevivir. Una FK obligaría a elegir entre
      impedir el borrado o borrar la historia de compras.
- [x] `LineTotal` guardado aunque sea `UnitPrice × Quantity`: es lo que se imprimió.
      Recalcularlo al leer parece más limpio hasta que cambia el redondeo y todos los
      comprobantes antiguos cuadran mal por un céntimo.
- [x] `decimal` con **precisión explícita** (18,2): la convención de EF hoy da eso mismo,
      pero dejarlo implícito hace que un cambio de convención mueva dinero sin que nadie lo
      note.
- [x] `ReceiptStatus` (`pending` / `available` / `failed`) + `ReceiptDocumentKey`. El
      estado existe porque la generación es asíncrona: sin él, «todavía no está» y «falló»
      serían indistinguibles.
- [x] Migración `AddOrdering` y revisarla antes de aplicarla (`rules.md` §11).

## 20.3 El puerto contra el catálogo

- [x] `ICatalogGateway.TryTakeAsync(sku, qty)` — **lo único** que `Ordering` necesita de
      `Catalog`. Apartar y consultar son la **misma** operación: separarlas es
      read-then-write y se vendería dos veces la última unidad.
- [x] `CatalogGateway` es **la única clase del slice que conoce `Catalog`**. El día que el
      catálogo sea otro servicio, cambia esa clase y nada más.

## 20.4 El caso de uso

- [x] `OrderService.PlaceAsync` con `ITransactionRunner`: apartar stock, crear la orden y
      encolar el evento **en la misma transacción**.
- [x] **Idempotencia reutilizada tal cual** de `planning/17`: `CommandIntent` obligatoria y
      `ICommandLog` dentro de la transacción. No hay mecanismo nuevo.
- [x] ⚠️ **Se agrupan las líneas repetidas antes de apartar stock.** Un carrito con el
      mismo SKU dos veces produciría dos líneas idénticas en el comprobante.
- [x] Un solo mensaje para «no existe» y «no hay bastante» (409): distinguirlos convierte
      el checkout en un inventario consultable desde fuera.
- [x] `GetForBuyerAsync` filtra por comprador **en la consulta** → 404, no 403. Decir
      «existe pero no es tuya» ya filtra que existe, y con ids correlativos eso permite
      contar las órdenes de la tienda desde fuera.

## 20.5 Mensajería: el broker deja de servir a un solo consumidor

**Esto es lo que había que resolver para que el PDF pudiera ser asíncrono**, y no es
código de órdenes: `Shared/Messaging` estaba escrito para **una** cola y **un** consumidor
(`RabbitMqOptions.Queue` / `.RoutingKey`, y `RabbitMqConnection` declarando esa topología
concreta).

Publicar `order.placed` sin tocar nada tenía un fallo concreto y silencioso: el publicador
usa `mandatory: true` con *publisher confirms*, así que un evento que **no encaja con
ninguna cola** vuelve como `312 NO_ROUTE` → el outbox lo cuenta como intento fallido y
acaba en `MaxPublishAttempts`. La compra funcionaría y el comprobante no se generaría
nunca.

- [x] `EventSubscription` (queue + routing key + dead-letter exchange) y
      `AddEventConsumer<T>(configuration, subscription)`: **el slice declara su cola**,
      `Shared` sigue poniendo solo el mecanismo y la condición de «hay broker».
- [x] `RabbitMqConnection` declara la topología de **todas** las suscripciones registradas.
- [x] ⚠️ **La cola del catálogo se declara con los argumentos EXACTOS de hoy.** Cambiar el
      `x-dead-letter-exchange` de una cola existente da **406 PRECONDITION_FAILED** y deja
      la mensajería abajo — es la trampa que ya se pisó con `RetryDelaySeconds`. Por eso el
      DLX es un campo de la suscripción y no una fórmula: el catálogo conserva el suyo
      heredado (`{Exchange}.dlx`) y los slices nuevos usan uno por cola.
- [x] ⚠️ **Una DLX por cola en los slices nuevos.** El DLX heredado es `fanout`: si las dos
      colas dead-letterearan a él, un fallo de órdenes aparecería **también** en la DLQ del
      catálogo. Es exactamente el bug de `planning/18` (el reintento copiándose a todas las
      colas de espera), y no se repite.
- [x] `OrderPlacedConsumer` — la misma fontanería AMQP que `ProductPurchasedConsumer`:
      ack manual, prefetch, contador propio de reintentos, DLQ.

## 20.6 La generación del comprobante

- [x] `IReceiptGenerator` = **el efecto**, separado del transporte, igual que
      `IProductPurchasedHandler`. Es lo que permite probarlo **sin broker** — la lección
      de `planning/18`: lo que vive dentro de un `BackgroundService` no se puede probar.
- [x] Se ejecuta dentro de `IMessageInbox.ProcessOnceAsync`: marca y efecto en la misma
      transacción, así que una reentrega no genera el documento dos veces y un fallo deja
      el mensaje para el reintento.
- [x] ⚠️ **El fichero se escribe DENTRO de la transacción y no se puede deshacer con
      ella.** Si el commit falla, queda un PDF huérfano en el almacén. Se acepta a
      sabiendas y en esta dirección: un fichero huérfano es basura recolectable —nadie lo
      referencia y no se puede alcanzar sin su clave—, mientras que un comprobante perdido
      es un cliente sin su documento. La alternativa (escribir después de confirmar) mueve
      el problema al otro lado y **sí** pierde documentos.
- [x] Si la orden ya tiene `ReceiptDocumentKey`, no se regenera: segunda red bajo la del
      inbox, y la que cubre un *replay* manual desde la DLQ.
- [x] `SetReceiptFailedAsync` **no** se llama desde el `catch`: el estado `failed` es para
      cuando ya no habrá más reintentos, no para cada intento fallido. Mientras haya
      reintentos pendientes el estado correcto es `pending`.
- [x] 🔴 …pero **eso dejaba el estado `failed` inalcanzable**, y lo destapó la revisión:
      `EventConsumer` gana un `OnExhaustedAsync` —el único punto en que «ya no habrá más
      intentos» es cierto— y `OrderPlacedConsumer` marca ahí la orden. Sin eso, un
      comprobante muerto en la DLQ dejaba `GET /{id}/receipt` devolviendo 409
      `receipt_not_ready` **para siempre**, o sea al cliente haciendo polling eterno sobre
      un documento que no iba a existir. Ahora responde `receipt_failed`, que no invita a
      reintentar. ⚠️ El aviso va **antes** del nack (morir después dejaría el hecho sin
      registrar) y **no puede lanzar**: si lanzara, el mensaje no llegaría a la DLQ, que es
      el sitio desde el que se recupera.
- [x] ⚠️ Y `SetReceiptFailedAsync` solo marca **si no hay clave**: el aviso de agotado puede
      cruzarse con un replay que sí terminó bien, y marcaría como fallido un comprobante
      que está descargándose.

## 20.7 QuestPDF — validación de la propuesta del owner

Lo pedía el owner y **se valida, no se acepta sin más** (2026-09-06):

- [x] **Licencia**: Community es gratuita, también comercialmente, con ingresos brutos
      anuales **por debajo de 1.000.000 USD**, y da 90 días de transición al superarlo. Se
      mira **primero** por lo que pasó con AutoMapper 15, que empezó a exigir licencia
      comercial con el proyecto ya montado. ⚠️ Es un umbral, no un «gratis para siempre»:
      **queda como decisión del owner** el día que aplique.
- [x] **API de composición en C#**, no HTML→PDF: sin navegador headless que instalar ni
      proceso externo que se cuelgue, y el maquetado se comprueba al compilar. Las
      alternativas serias eran iText7 (AGPL o licencia comercial **desde el primer día**) y
      un `wkhtmltopdf`/Chromium (proceso externo, arranque por documento, y hay que
      empaquetar un navegador en la imagen). Para alta transaccionalidad, la diferencia no
      es de estilo.
- [x] Detrás de `IReceiptRenderer`: **la librería es sustituible**. Ni el consumidor, ni el
      controller, ni el dominio la nombran.
- [x] ⚠️ **La licencia se declara al ARRANCAR o lanza al generar.** Sin cuidarlo, la API
      arrancaría sana y los comprobantes fallarían uno a uno dentro del consumidor.
- [x] ⚠️ **En Linux dibuja con SkiaSharp y necesita `libfontconfig1`**. La imagen
      `mcr.microsoft.com/dotnet/aspnet` no la trae: sin añadirla al `Dockerfile`, esto
      revienta **solo dentro del contenedor** —en local funciona—, que es la peor forma de
      descubrirlo.
- [x] ⚠️ **Sin `FontFamily` explícita.** QuestPDF **embebe** su fuente por defecto (Lato)
      en el paquete: el documento sale idéntico en local y en la imagen. Pedir Calibri
      —que no existe en Linux— deja el resultado a merced de la sustitución de fuentes de
      cada máquina.
- [x] Cultura **invariante** en el documento: con la cultura ambiente, el mismo comprobante
      saldría con coma o con punto decimal según la réplica que lo generara.

## 20.8 El acceso — que no se pueda adivinar

- [x] `GET /api/v1/order/{id}/receipt` sirve el PDF **por endpoint**, nunca como estático.
      Hay tres barreras y hacen falta las tres: fuera de `wwwroot/` (no hay URL pública),
      clave aleatoria (no se adivina) y el filtro por comprador (no se lee la de otro).
- [x] Comprobante de otro → **404**. Sin generar → **409** con `code` `receipt_not_ready`,
      que es reintentable. Sin token → **401**.
- [x] La clave del documento **no se expone en el DTO**: es un detalle del almacén. Lo que
      ve el cliente es `receiptStatus`.

## 20.9 Verificación (`rules.md` §11)

- [x] `dotnet build` sin warnings.
- [x] Tests nuevos: almacén (traversal incluido), render (que salga un PDF de verdad),
      generador (con el efecto fallando y con la reentrega), y de integración: comprar,
      ver la orden, la orden de otro, el comprobante no listo, la misma `Idempotency-Key`.
- [x] **Ejecutando de verdad**, no solo compilando: la compra concurrente sobre el mismo
      stock y el ciclo completo compra → evento → PDF contra el RabbitMQ real.

---

## 20.10 Lo que NO entra, y por qué

- **Estados de la orden más allá de `Paid`.** El `enum` está completo porque cambiarlo
  después migra datos, pero no hay endpoints de transición: eso es `Shipping`, otro
  contexto.
- **Descuentos, impuestos y envío calculados.** Los campos existen y van a cero. Están en
  el modelo porque el desglose es parte del documento y añadirlos luego obligaría a migrar.
- **Cancelar o devolver.** Devolver stock a la orden cancelada es una operación con sus
  propias invariantes; no se improvisa aquí.
- **Numeración sin huecos.** La secuencia no se deshace con un rollback, así que una
  compra fallida o un reintento de EF se llevan un número. Para un comprobante interno da
  igual; para una **factura** varias legislaciones exigen correlatividad sin huecos, y eso
  exige una tabla de contadores por serie bloqueada dentro de la transacción — o sea pagar
  serialización justo donde más contención hay. Queda dicho en `OrderRepository`.
- **Recolección de documentos huérfanos.** Un job que borre lo que ninguna orden
  referencia. Anotado en §20.6; hoy no hay volumen que lo justifique.

---

## 20.11 Lo que apareció EJECUTANDO (y no compilando)

Tres cosas, todas medidas el 2026-09-06 contra SQL Server, Redis y RabbitMQ reales:

1. 🔴 **El comprobante se descargaba con el nombre de la clave opaca**
   (`cdfdcf87c326aadb22845f2f46c8c691.pdf`). El almacén solo sabe eso del documento, así
   que devolvía lo único que tenía. Cómo se llama un documento **de cara al usuario** es
   conocimiento del dominio: lo pone `OrderService` (`content with { FileName = … }`), y
   de paso deja de filtrar la forma de las claves. Ahora baja como `ORD-2026-000001.pdf`.

2. 🔴 **Cada 4xx de dominio escribía un «An unhandled exception has occurred» a nivel
   Error con traza completa.** Medido: 30 compras simultáneas sobre stock 20 dejaban 20
   órdenes… y **10 incidentes falsos**, uno por cada rechazo legítimo por falta de stock.
   Un 404 de categoría hacía lo mismo, así que **es previo a esta tarea** y no del slice.
   Es la misma familia que `planning/19` y se le escapó: aquello miró los cortes de cliente
   y los timeouts de base, no las excepciones de dominio.
   La línea del framework es además **un duplicado**: `GlobalExceptionHandler` ya registra
   todo, y mejor —Error con traza para ≥500, Warning de una línea para 4xx, las dos con el
   `CorrelationId`—. Silenciada por configuración. De 10 «errores» a **0**.
   - ⚠️ `SuppressDiagnosticsCallback` (lo suyo para esto) es de **.NET 10**; aquí el
     runtime es 9, así que se hace con un `MinimumLevel:Override` a `Fatal` — Serilog no
     tiene nivel `None`.
   - ⚠️ Y una trampa dentro de la trampa: **dentro de `MinimumLevel:Override` NO se pueden
     poner claves `//`**. Serilog resuelve *cada* clave como nombre de logger y el arranque
     muere con `No LoggingLevelSwitch has been declared with name "…"`. El comentario vive
     un nivel más arriba.
   - Como ahora el handler es **el único** que registra excepciones, dos tests nuevos fijan
     que lo siga haciendo (Error con traza para un 500, Warning y **no** Error para un 4xx).
     Sin ellos, quitar ese log no rompería nada y perderíamos todas las trazas de 500.

3. **La fuente**: se pedía Calibri, que **no existe en Linux**. Se quita la familia
   explícita y se usa la que QuestPDF **embebe** (Lato). Verificado en el PDF generado:
   `MediaBox [0 0 595 842]` (A4) y tres subsets embebidos (`Lato-Regular`, `-Bold`,
   `-SemiBold`), así que el documento sale igual en local que en la imagen.

### Verificación ejecutando — resultados

| Qué | Resultado |
|---|---|
| Compra → outbox → RabbitMQ → consumidor → PDF | ✅ 31 KB, `%PDF-1.4`, A4, fuente embebida |
| **30 compras simultáneas sobre stock 20** | ✅ **20 órdenes con 20 números ÚNICOS** + 10×409. La secuencia no reparte repetidos |
| 22 órdenes → 22 comprobantes | ✅ todas `available`, 22 ficheros en el almacén |
| Misma `Idempotency-Key` | ✅ misma orden, `Idempotency-Replayed: true`, stock descontado una vez |
| Comprobante recién pedido | ✅ 409 `receipt_not_ready` |
| Orden y comprobante de otro | ✅ 404 (no 403) |
| Sin token | ✅ 401 |
| Suite | ✅ **246 tests** (eran 202), build sin warnings |

✅ **Esto se daba por no verificable y sí se pudo**: ver §20.13. La clave era que no hacía
falta tumbar SQL Server — bastaba una raíz de almacén sin permiso de escritura.

✏️ **Una consecuencia del replay que conviene tener dicha**: repetir la compra con la misma
`Idempotency-Key` devuelve la respuesta **original**, o sea con `receiptStatus: "pending"`
aunque el comprobante ya esté listo. Es lo correcto —reproducir, no recalcular; es lo que
hace Stripe— y el cliente ve el estado actual con un `GET /api/v1/order/{id}`. Pero si
alguien lo lee del replay, se lleva un dato viejo.

---

## 20.12 Revisión multiagente (`rules.md` §9)

Dos revisores en paralelo —concurrencia/mensajería y seguridad/acceso—, los dos con la
instrucción de **verificar ejecutando**. Entre los dos encontraron cinco cosas que el build
y los 246 tests no veían:

| | Hallazgo | Estado |
|---|---|---|
| 🔴 | **`ReceiptStatus.Failed` inalcanzable** → 409 `receipt_not_ready` eterno | corregido (`OnExhaustedAsync`) |
| 🔴 | **Deadlock evitable**: el stock se descontaba en el orden del carrito. A compra `[1,2]` y B `[2,1]` a la vez → 1205 | corregido: se ordenan las líneas por SKU. Un orden total de adquisición lo hace imposible por construcción |
| 🟠 | **La canonicalización no seguía enlaces simbólicos**, y el comentario decía que sí | corregido: se resuelve segmento a segmento. Reproducido por el revisor (`2026 → ../secretos` leía fuera) |
| 🟠 | **Una barra final en `Documents:RootPath` rompía el almacén entero, en silencio** | corregido (`TrimEndingDirectorySeparator`) |
| 🟠 | **`?page=2147483647` → 500** (`(Page-1)*PageSize` desborda a negativo, y SQL rechaza un OFFSET negativo) | corregido saturando. **Previo**, afecta a todos los `/paged` |

Y cuatro menores, también aplicadas: el change tracker quedaba sucio tras el `catch` de
intención duplicada (se limpia en `TransactionRunner`, así que arregla también
`ProductService`); `OpenAsync` tenía un TOCTOU que salía como 500 en vez del 404 que la
interfaz promete; un fallo a mitad de `SaveAsync` dejaba un PDF **truncado** en disco; y el
comprobante se servía sin `Cache-Control: no-store`. Más `[RequestSizeLimit]` en la compra
y la raíz del almacén **rechazada al arrancar si cae dentro de `wwwroot`**, que es la
premisa entera de la feature y hasta ahora no la comprobaba nada.

**Lo que confirmaron que estaba bien**, con su prueba, para que no se «arregle» luego: la
refactorización a `EventConsumer<,>` es fiel al consumidor anterior (diff mecánico); el
cuerpo de `PlaceAsync` **es replayable** (el `ExecuteUpdate` del stock va por el mismo
`DbContext`, luego por la misma transacción, y se deshace con ella); `SetReceiptAsync`
participa de verdad en la transacción del inbox; y no hay cruce de colas ni de DLQ
—verificado contra el broker real, no deducido—. Sin IDOR, sin enumeración, sin fuga de la
clave del documento, y `App_Data` fuera del alcance de `UseStaticFiles`.

✏️ **Y una decisión que pasa a estar escrita**: `CustomerName` lo elige el cliente y se
imprime tal cual. Es un dato de envío («¿a nombre de quién va el paquete?») y nadie más
puede descargar ese PDF, pero significa que **el documento no vale como prueba de identidad
de nadie**. El día que sea una factura fiscal, el nombre sale del perfil verificado.

## 20.13 Y una verificación que sí se pudo hacer

`planning/20` §20.11 daba por no verificable el ciclo de reintentos con un fallo real,
porque exigía tumbar SQL Server. **No hacía falta**: basta apuntar `Documents:RootPath` a
una ruta sin permiso de escritura, que hace fallar el efecto y deja intacto todo lo demás.

Medido, con `MaxDeliveryAttempts=2` y `RetryDelaySeconds=2`:

```
[WRN] Failed to process 9ae80b8b… (attempt 1/2); retrying in 2s
[ERR] Failed to process 9ae80b8b… after 2 attempt(s) -> DLQ
```

- la orden pasó a **`failed`** y `GET /{id}/receipt` devolvió **409 `receipt_failed`**;
- **la compra siguió siendo válida** (`paid`, total y líneas intactos): que no se pueda
  imprimir un papel no invalida algo ya cobrado, que es el escenario del `.feature`;
- el mensaje muerto apareció en `apiecommerce.order-placed.dlq` (1) y **no** en
  `apiecommerce.product-purchased.dlq` (0): la DLX por cola hace lo que promete.

**261 tests** en verde tras aplicar todo lo anterior (eran 246).
