# AGENTS/progress.md — Bitácora de avance y pendientes

> Qué está hecho, qué está a medias y qué falta, con la evidencia de cómo se verificó.
> **Se actualiza en el mismo commit que el código.** El diseño objetivo vive en
> `docs/06-estado-y-roadmap.md`; esto es la foto de ejecución.

Última actualización: **2026-09-06** (órdenes y comprobante en PDF: cuarto contexto acotado).

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
| 20 | **Órdenes y comprobante en PDF** | ✅ | [`features/20`](features/20_ordenes-y-comprobante.feature) · [`planning/20`](planning/20_ordenes-y-comprobante.md) | — |

**Ya no queda ningún ⚠️.** El 09 se cerró el 2026-09-05 contra un RabbitMQ real, y esa
verificación destapó un bug que el build y el smoke test no veían (abajo).

---

## 2. Bitácora

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

1. **Tests** ([`planning/11`](planning/11_proyecto-de-tests.md)) — fases 1–5 hechas
   (153 tests). Falta la **fase 6, CI**: hoy nada corre `dotnet build` ni `dotnet test`
   antes de un merge, así que la red existe pero nadie la obliga a estar tendida.
   Pendiente también migrar la fase 3 a Testcontainers **en el runner**, donde sí hay
   Docker.
2. **Deuda de la revisión** ([`planning/12`](planning/12_deuda-revision-multiagente.md)) —
   reintentos del consumidor sin contador real, outbox sin claim para multi-réplica, purga
   de tablas, `ETag`/`If-Match`, hash del cuerpo en la clave de idempotencia.
3. **Refresh tokens** ([`planning/13`](planning/13_refresh-tokens.md)).
4. **Administración de usuarios** ([`planning/14`](planning/14_admin-usuarios.md)) — hoy el
   único camino para tener un admin es el seeder.
5. **Partir en proyectos** ([`planning/15`](planning/15_partir-en-proyectos.md)) —
   diferido a propósito: hacerlo antes de que el proyecto lo pida solo añade fricción.

---

## 5. Decisiones que esperan al owner

| Tema | Pregunta |
|---|---|
| **Licencia de AutoMapper** | La 15.1.1 exige licencia comercial en producción (avisa por log). ¿Comprar, fijar ≤13.x (última MIT), o migrar a Mapperly? |
| **Política de commits** | `rules.md` §12 dice que el agente commitea (práctica de este repo). En el repo de frontend del owner la regla es la contraria. ¿Se confirma? |
| 🔴 **Token de GitHub en `.git/config`** | El remoto es `https://ghp_…@github.com/AlexMartin998/dotnet-ecommerce-v1.git`: un **PAT en texto plano** que aparece en cualquier `git remote -v`. **Revocarlo en GitHub** (Settings → Developer settings → Personal access tokens) y volver a autenticar con `gh auth login` o con SSH. Es lo más urgente del repo. |
| **Secretos ya en el historial** | Resuelto para adelante (user-secrets), pero la clave JWT y la password de SQL **siguen en los commits anteriores**. La JWT ya se rotó al migrar; la de SQL es la del contenedor local compartido. Limpiar el historial (`git filter-repo`) solo compensa si el repo se hace público. |
| **Migrar a `net10.0`** | Resuelto por ahora instalando el runtime 9 (el owner quiso mantener el 10 para lo demás). Sigue abierto a futuro: alinearía el proyecto con el SDK y con `dotnet-ef` 10, hoy desalineados. |
