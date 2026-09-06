# 18 — Cerrar la deuda de mensajería (`planning/12` §12.5)

> Lo que quedaba de la segunda revisión multiagente, más lo que ya no cuadraba después de
> `planning/17`. Spec: [`features/18`](../features/18_mensajeria-robusta.feature).

---

## 18.1 🔴 El test del P0 del consumidor — y por qué no se podía escribir

El P0 de la revisión: la marca de idempotencia se confirmaba **antes** del efecto, así que
si el efecto fallaba, la reentrega se reconocía como duplicado, se hacía ack y **el mensaje
desaparecía sin procesarse**. Se corrigió metiendo marca y efecto en una transacción, y se
verificó que no rompe el camino feliz. Pero **falta el test que fuerce un efecto fallido**,
que es el único caso para el que existe todo el mecanismo de reintentos.

No se podía escribir porque `ProcessAsync` era un método privado del consumidor que solo
escribía un log: no había forma de hacerlo fallar. Y el consumidor es un
`BackgroundService` atado a un broker, así que probarlo de punta a punta exigiría RabbitMQ
en la CI, que no está.

**El patrón se repite en casi todo este planning**: lo que no se podía probar era lo que
vivía dentro de un `BackgroundService`. Sacar de ahí el efecto, la unidad transaccional y
el contador de intentos es lo que convierte tres cosas «verificadas a mano» en diez tests.

- [x] **Efecto extraído a `IProductPurchasedHandler`** (`LowStockNotifier`), en el slice
      de `Catalog`. El consumidor pasa a ser fontanería AMQP pura.
- [x] **Unidad transaccional extraída a `IMessageInbox`** — el gemelo de `IEventOutbox`,
      y el mismo patrón que `ICommandLog` para HTTP. Los tests **no necesitan broker**.
- [x] **4 tests**: efecto que falla → sin marca; segunda pasada → **reejecuta de
      verdad**; reentrega de algo procesado → no reejecuta; dos simultáneas → una sola
      aplica el efecto y la perdedora es reconocible como choque de clave primaria (de eso
      depende el consumidor para hacer ack en vez de gastar un reintento).

## 18.2 🟠 El contador de reintentos vive en `x-death`, que no es nuestro

Dos problemas por el mismo motivo — el contador lo escribe el broker y nosotros solo lo
leemos:

- **Un replay desde la DLQ no resetea el presupuesto.** `x-death` sobrevive al paso por la
  DLQ, así que un mensaje que un operador reencola vuelve con el contador agotado y muere
  en la primera entrega. La herramienta que existe para recuperar mensajes no los recupera.
- **El parseo es frágil**: `x-death` es una lista de diccionarios cuyos valores de texto
  viajan como `byte[]`; compararlos contra un `string` sin convertir devuelve `false` en
  silencio (ya pasó). Y filtra por **nombre de la cola de reintento**, que §18.3 va a
  cambiar.

- [x] **Contador propio** en `x-retry-attempt`, extraído a `RetryAttempts` — una función
      **pura**, y por eso con 6 tests unitarios sin broker. Cubren el error que ya se
      cometió con `x-death` (un valor de texto llega como `byte[]` y compararlo sin
      convertir devuelve `false` en silencio) y el procedimiento de replay: borrar la
      cabecera devuelve el presupuesto completo.

## 18.3 🟠 `RetryDelaySeconds` es inmutable tras el primer despliegue

El TTL vive en `x-message-ttl` de la cola de reintento, que se fija al declararla.
Cambiarlo da **406 PRECONDITION_FAILED** y cierra el canal: habría que borrar la cola en
producción, con sus mensajes dentro.

- [x] **Nombre de la cola versionado con su TTL** (`…retry.7s`). Verificado ejecutando:
      con la cola de 7 s ya declarada, arrancar con el plazo a 12 s **no da 406** — declara
      `…retry.12s` y sigue.
      ⚠️ Se descartó poner el TTL **en el mensaje**: en una cola FIFO, un mensaje con TTL
      largo bloquea a los de detrás (head-of-line blocking) aunque ya hayan caducado.
- [x] 🔴 **Y un defecto que abrió este mismo cambio, encontrado EJECUTANDO.** Con las colas
      de espera ligadas a un exchange, cada reintento se copiaba a **todas** —incluidas las
      de plazos anteriores, que siguen existiendo—. Medido: un solo reintento apareció a la
      vez en las tres. El inbox lo deduplica, así que no se ejecutaba de más, pero
      multiplicaba el tráfico y hacía ilegible qué estaba pasando.
      Arreglado publicando el reintento al **exchange por defecto** con el nombre de la
      cola como routing key: sin binding, sin fan-out, y además es lo que de verdad se
      quiere decir. `RetryExchange` desaparece — nunca aportó enrutado, solo tenía un
      binding.

## 18.4 🟠 Un canal AMQP por mensaje publicado

`RabbitMqEventPublisher` abre y cierra un canal en cada `PublishAsync`. Abrir un canal es
un viaje de ida y vuelta al broker: con `Outbox:BatchSize` en 50, son 50 canales por vuelta
del publicador.

- [x] **Canal reutilizado**, recreado si se cierra, con el acceso serializado por un
      semáforo (los `IChannel` **no son thread-safe**). Medido: 30 eventos publicados
      pasando de 1 a **2** canales abiertos, no a 30.

## 18.5 🟠 CI y entorno de trabajo compilan con SDK distintos

Aquí el SDK es 10.0.400 y en CI es 9.0.x, sin `global.json`. «0 warnings» se mide con
analizadores distintos en cada sitio, así que la CI puede fallar por algo que aquí no sale
—o al revés, que es peor.

- [x] `global.json` fija la banda **10**, que es la única que hay en el dev container, y
      la CI pasa a instalar **los dos** SDK: el 10 porque es el que fija `global.json`, y
      el 9 porque el SDK 10 **no trae el runtime 9** y sin él `dotnet test` compila pero no
      puede ejecutar. ⚠️ Fijar la banda 9 no valía: `latestFeature` no salta de major, así
      que aquí no habría resuelto ningún SDK.

## 18.6 ⚪ El orden real de publicación del outbox — **no-goal deliberado**

`Sequence` es determinista y repetible pero **no garantiza el orden**: el `IDENTITY` se
asigna al `INSERT` y la fila se ve al `COMMIT`, así que una transacción lenta con secuencia
menor puede confirmar después de que ya se publicara una mayor (verificado).

- [x] **Documentado como decisión, no como pendiente**, en el XML doc de
      `OutboxMessage.Sequence` —donde lo va a leer quien toque eso— y no solo aquí. Hoy hay un evento por compra y
      ningún consumidor depende del orden entre agregados. Arreglarlo de verdad pide un
      *watermark* que espere a las transacciones abiertas —no una columna— y eso es
      complejidad real a cambio de nada.
      **Señal para reabrirlo**: el día que un consumidor necesite ver dos eventos del
      mismo agregado en orden (`ProductUpdated` seguido de `ProductPurchased`).

---

## 18.7 Verificación

- [x] `dotnet build -warnaserror` sin warnings · **183 tests** en verde (eran 171).
- [x] **Ejecutando, contra el RabbitMQ real** (`192.168.3.82:5672`, UI en `:15672`):

| Qué | Resultado |
|---|---|
| Cambiar `RetryDelaySeconds` de 7 a 12 con la cola ya declarada | arranca sin 406 y declara `…retry.12s` |
| Un mensaje puesto en la cola de espera | caduca, vuelve solo a la principal y se procesa **una** vez |
| Reintento programado con colas de plazos anteriores presentes | entra **solo** en la vigente (antes: en las tres) |
| 30 compras seguidas | 30 eventos consumidos, **1 canal** de publicación, 0 errores |
| Bindings de la cola de espera | solo el implícito del exchange por defecto |

⚠️ **Lo que NO se verificó de punta a punta**: el ciclo completo de reintentos con un
efecto que falla de verdad. Todos los fallos de *contenido* (tipo inesperado, cuerpo
ilegible) van directos a la DLQ **por diseño**, así que el único disparador del reintento
es un fallo de infraestructura, y provocarlo aquí exigía tumbar SQL Server —que es
compartido—. Se cubre en dos mitades: el comportamiento transaccional con los 4 tests del
inbox, y el contador con los 6 de `RetryAttempts`. Queda dicho para no dar por probado
más de lo que se probó.
