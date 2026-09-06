# 21 — Recuperar lo que murió en la DLQ, y recoger la basura del almacén

> Contrato: [`features/21`](../features/21_recuperar-comprobantes-y-recoger-basura.feature).
>
> Cierra las dos deudas que dejó abierta `planning/20` (§20.6 y §20.10). Son **dos mitades
> del mismo problema**: que un efecto pueda fallar sin dejar ni trabajo perdido ni basura.
>
> Hoy `ReceiptStatus.Failed` es un **callejón sin salida**: el aviso de agotado marca la
> orden, el cliente deja de hacer polling —eso ya está bien—, pero **reemitir el comprobante
> exige entrar al broker a mano**. Y el almacén no tiene quien recoja lo que nadie apunta.

---

## 21.0 La decisión que ordena el resto

**Reemitir es una decisión humana, no un job.** Si un mensaje agotó sus intentos es porque
algo estaba roto de verdad: reencolarlo automáticamente solo repite el fallo y convierte la
DLQ en un bucle caro. Por eso esto es un **endpoint de administración** y no un
`BackgroundService`.

El recolector de basura sí es un job, porque su decisión —«esto no lo referencia nadie»— es
mecánica y no necesita criterio.

---

## 21.1 La DLQ deja de ser un agujero

- [x] `IDeadLetterAdmin` en `Shared/Messaging/RabbitMq/`: `GetStatusAsync()` (una fila por
      cola registrada con cuántos mensajes tiene parados) y `ReplayAsync(queue, max, ct)`.
- [x] ⚠️ **La cola se identifica por nombre, y el nombre viene en la petición**, así que se
      valida contra las `EventSubscription` **registradas**: es una allowlist por
      construcción. Sin eso el endpoint mueve mensajes de **cualquier** cola del broker —
      incluidas las de otros proyectos, porque el broker es compartido. Cola desconocida →
      **404**, no 400: para quien llama, esa cola no existe.
- [x] ⚠️ **Publicar primero, confirmar después.** Al revés, morir entremedias pierde el
      mensaje: ya estaría confirmado en la DLQ y aún no publicado. En este orden, morir
      entremedias provoca como mucho una reentrega, y el inbox la deduplica. Es la misma
      regla que `ScheduleRetryAsync`.
- [x] ⚠️ **El contador de intentos se reinicia a cero.** Si no, el mensaje vuelve con el
      presupuesto gastado y muere en la primera entrega: la herramienta para recuperar
      mensajes no recuperaría ninguno. Es justo la razón por la que el contador es **nuestro**
      (`RetryAttempts`) y no `x-death`, que sobrevive al paso por la DLQ.
- [x] **Se publica al exchange principal con la routing key de la suscripción**, no
      directamente a la cola: así el mensaje recorre el mismo camino que uno nuevo.
- [x] `BasicGetAsync` acotado por `max` y no un consumidor: la operación tiene que
      **terminar**, y quien la lanza necesita saber cuántos movió.
- [x] `[Authorize(Roles = Roles.Admin)]`. Sin broker configurado → **503**.
- [x] ⚠️ **No se expone el contenido de los mensajes.** Un `order.placed` no lleva datos
      personales, pero un endpoint que vuelca payloads de la DLQ es una fuga esperando a que
      alguien publique un evento más rico. Se exponen **recuentos**, que es lo que hace falta
      para decidir.

**Qué NO entra**: purgar la DLQ (destructivo y sin vuelta atrás; se hace en la consola del
broker, a sabiendas) y reemitir un mensaje concreto por id (AMQP no permite tomar uno del
medio de una cola sin recorrerla).

## 21.2 El recolector de documentos huérfanos

- [x] `IDocumentStore.ListAsync(DateTime writtenBefore, ct)` → `IAsyncEnumerable<...>`.
      Es una operación que **todo** almacén sabe hacer (S3 `ListObjects`, R2, MinIO,
      Cloudinary), y se devuelve como flujo para no materializar un bucket entero.
- [x] `ReceiptCleaner` (`BackgroundService`) **en `Features/Ordering/Documents/`**, no en
      `Shared/`: la pregunta «¿quién referencia esta clave?» solo la sabe responder quien
      tiene la tabla, y `Shared/` no puede nombrar tipos de `Features/` (`rules.md` §4).
      Hoy `Ordering` es el único que escribe documentos; el día que haya otro, esto se
      invierte con un puerto.
- [x] `IOrderRepository.FindReferencedKeysAsync(keys, ct)` — **por lotes**, no una consulta
      por fichero.
- [x] ⚠️ 🔴 **PERIODO DE GRACIA, y es la propiedad que no se puede equivocar.** El PDF se
      escribe **dentro** de la transacción, así que entre que el fichero existe y existe la
      fila que lo apunta hay una ventana. Un recolector sin gracia borraría comprobantes
      **buenos a mitad de vuelo**. Solo se mira lo escrito hace más de `OrphanGraceHours`
      (por defecto 24 h, tres órdenes de magnitud por encima de la ventana real).
- [x] **Ante la duda, no se borra**: si la consulta de referencias falla, el lote se salta
      entero. Borrar de más es perder el documento de un cliente; borrar de menos es
      quedarse con basura una vuelta más.
- [x] Un **PDF truncado** no necesita caso especial: como `SaveAsync` nunca devolvió clave,
      nadie lo referencia y cae por la misma regla. (`planning/20` §20.11 decía que el
      recolector tendría que distinguirlo — **no hace falta**, y queda corregido.)
- [x] ⚠️ El bucle va en `try/catch` **sin filtro que excluya `OperationCanceledException`**:
      un `BackgroundService` que lanza muere y no vuelve, y desde .NET 6 se lleva el host por
      delante.

## 21.3 Configuración

- [x] `DocumentStorageOptions` gana `OrphanGraceHours` y `CleanupIntervalHours`, con
      `[Range]` y `ValidateOnStart` como el resto. Van ahí y no en una sección nueva porque
      son del **ciclo de vida del documento**, igual que `Outbox:RetentionDays` gobierna el
      del outbox.
- [x] **`CleanupIntervalHours = 0` apaga el recolector.** Un job que borra ficheros tiene que
      poder apagarse sin desplegar.

## 21.4 Verificación

- [x] `dotnet build -warnaserror` limpio y suite en verde.
- [x] Tests del recolector con el almacén real: borra el huérfano, **no** borra el
      referenciado, **no** borra el reciente, y sobrevive a un fallo del almacén.
- [x] Tests del admin de DLQ sin broker (503) y de la allowlist (404).
- [x] **Ejecutando de verdad contra RabbitMQ**: provocar un comprobante fallido como en
      `planning/20` §20.13 (raíz sin permiso de escritura), comprobar que queda en `failed`
      y en la DLQ, **arreglar** el almacén, reemitir por el endpoint, y ver la orden pasar a
      `available` con su PDF descargable. Es el ciclo completo de recuperación.

---

## 21.5 Verificado ejecutando (2026-09-06)

El ciclo completo de recuperación, contra SQL Server, Redis y **RabbitMQ reales**. Se
provoca el fallo como en `planning/20` §20.13 —`Documents:RootPath` a una ruta sin permiso
de escritura— y luego se arregla:

| Paso | Resultado |
|---|---|
| Comprobante que agota sus 2 intentos | orden `ORD-2026-000029` → **`failed`** |
| `GET /api/v1/dead-letter` | **2** en `apiecommerce.order-placed.dlq`, **0** en la del catálogo |
| `POST /dead-letter/apiecommerce.otro-proyecto/replay` | **404** — la allowlist funciona |
| Almacén arreglado + `POST /dead-letter/apiecommerce.order-placed/replay?max=10` | `{"replayed": 2}` |
| La orden, 8 s después | **`available`**, y el PDF baja (29 KB, `ORD-2026-000029.pdf`) |
| Las dos DLQ | **0 mensajes** |
| Log | **0 errores** |

Y **274 tests** en verde (eran 262), build con `-warnaserror` sin warnings. Los del
recolector usan el almacén y la base reales; los dos que de verdad importan son los que
comprueban que **no** borra —el referenciado y el recién escrito—, y se pueden escribir
porque el efecto vive fuera del `BackgroundService`.

⚠️ **Lo que NO se verificó ejecutando**: que un mensaje reemitido que **vuelve a fallar**
tenga otra vez sus N intentos. El contador se pone a cero en el código y `RetryAttempts`
tiene sus 6 tests, pero el ciclo entero —reemitir algo que falla de nuevo— no se provocó.

## 21.6 Lo que sigue sin entrar

- **Purgar la DLQ** desde la API: es destructivo y sin vuelta atrás. Se hace en la consola
  del broker, a sabiendas y con las manos.
- **Reemitir un mensaje concreto por id**: AMQP no permite tomar uno del medio de una cola
  sin recorrerla. Con volumen, la respuesta correcta es una tabla de mensajes muertos, no
  un endpoint más listo.
- **Alertar cuando la DLQ crece.** Hoy hay que mirar. La sonda `outbox-backlog` ya hace algo
  parecido para el outbox y sería su gemela; se deja fuera porque alertar sin destinatario
  no es alertar.
