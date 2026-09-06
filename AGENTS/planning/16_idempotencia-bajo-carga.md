# 16 — Afinar la idempotencia para carga alta

> ⚠️ **Continúa en [`planning/17`](17_idempotencia-transaccional.md)**, que cierra el
> hallazgo principal de este documento cambiando dónde vive la garantía.

> Fase de **revisión y afinamiento**. No hay feature nueva: se valida exhaustivamente lo
> que ya existe (`planning/08`) contra infraestructura real y se corrige lo que la carga
> destapa. Spec: [`features/16`](../features/16_idempotencia-bajo-carga.feature).
>
> Todo lo de aquí se midió el **2026-09-06** contra SQL Server, Redis 7.0.15 y RabbitMQ
> reales en `192.168.3.82` (la IP del host cambió; antes `172.17.0.1`, que sigue
> resolviendo porque es la puerta del bridge de Docker).

---

## 16.0 Lo que se validó y **aguanta** (no tocar)

Con la API arrancada de verdad, no con tests:

- [x] **Exactamente-una-vez en un proceso.** Ráfagas simultáneas de 60, 150, 250 y 350
      peticiones con la misma clave sobre un SKU aislado: el stock baja **1**. 4/4.
- [x] **Exactamente-una-vez entre DOS RÉPLICAS** (dos procesos, `:8021` y `:8022`, mismo
      Redis y misma base). 80 peticiones simultáneas alternando instancia: baja **1**. 4/4.
      Es la razón por la que el store vive en Redis y no en memoria, y hasta hoy no se
      había comprobado.
- [x] **Contrato semántico completo**: replay con cuerpo idéntico y cabecera
      `Idempotency-Replayed: true`; 422 al reusar la clave con otro cuerpo; aislamiento
      entre usuarios; el error **no** se memoriza; la clave está acotada por ruta.
- [x] **Coste del filtro**: no se mide por encima del ruido del cliente. Medido con un
      cliente asyncio sobre sockets crudos, 1000 peticiones: a 16 conexiones, 276 rps con
      clave contra 317 sin ella.
      ⚠️ **Afirmación retirada**: una primera medición con 200 hilos de `urllib` dio
      «13× menos throughput con la clave». Era el **arnés**, no la API — el mismo pico
      de 20 s aparecía en la columna *sin* clave. No se apunta un número que no se
      reproduce con un cliente limpio.

---

## 16.1 🔴 El hallazgo: bajo carga, la garantía **se apaga sola**

`CachingExtensions.Connect` fija `SyncTimeout`/`AsyncTimeout` en **1000 ms**. Es una
decisión buena y documentada, tomada para el caso **«Redis caído»**: con los valores de
fábrica una compra con `Idempotency-Key` tardaba 34 s.

Pero con Redis **vivo y sano**, una ráfaga basta para agotar esos 1000 ms: hay **un solo
multiplexer** y el filtro gasta **3 operaciones por petición**. Cuando el `SET NX` expira,
`TryAcquireAsync` captura la excepción y **devuelve `true`**: la petición se ejecuta *sin
garantía de idempotencia*, en silencio.

**Medido** (`grep "proceeding without guarantee"`):

| Escenario | Peticiones sin garantía |
|---|---|
| 1000 peticiones, 64 conexiones | 26 (2,6 %) |
| ~1040 peticiones, 260 concurrentes | 229 (22 %) |
| 1000 peticiones, 128 conexiones | 0 — **es a ráfagas, no gradual** |

Por qué importa: el fallo se concentra en el **acquire**, y ahí sólo hace daño si el
timeout cae sobre una petición **duplicada** — que es exactamente lo que pasa en el caso
patológico real: *la API va lenta → el cliente reintenta → el reintento entra en la
ventana saturada*. La protección se desactiva justo cuando hace falta.

⚠️ **No se consiguió provocar una duplicación real** en ~20 tandas. El camino de código
está confirmado y contado; la duplicación exige que el timeout caiga sobre el duplicado,
y los duplicados son una fracción minúscula del tráfico. Se reporta como lo que es:
**mecanismo demostrado, probabilidad baja, impacto alto (doble cobro)**.

### Qué se hace

- [x] **Bajar el coste por petición.** `SET clave valor EX ttl NX GET`
      (Redis ≥ 7.0, el nuestro es 7.0.15; `StringSetAndGetAsync` existe en
      StackExchange.Redis 2.8.58) **reserva si no existe y devuelve lo que había si sí**.
      Eso hace innecesario el `GetAsync` previo. Menos presión sobre el multiplexer =
      menos timeouts = menos fail-open.
      - Medido: **3,00 → 2,00 viajes por petición** (−33 %). Los comandos que Redis
        contabiliza suben a 4,00, pero dos de ellos corren **dentro** del script Lua y
        no son un viaje más: `commandstats` cuenta comandos, no idas y venidas.
- [x] **Hacer visible el fail-open.** `IdempotencyOutcome.Unavailable` se distingue de
      `Acquired` (antes los dos eran el mismo `true`), la respuesta sale con
      `Idempotency-Guaranteed: false` y sube el contador
      `apiecommerce.idempotency.requests{outcome=unguaranteed}`.
- [x] **NO invertir la decisión** a fallar en cerrado sin el owner: `rules.md` §8 exige
      que se invierta explícitamente y en las dos implementaciones a la vez. Va a §16.6.

---

## 16.2 🟠 El `422` no cubre a quien pierde la reserva por poco

`IdempotentAttribute.OnActionExecutionAsync` compara la huella del cuerpo al principio,
pero el camino que reproduce una respuesta **recién terminada** —el que corre cuando
`TryAcquireAsync` falla porque la clave ya existe— llama a `Replay` **sin comparar nada**.

Interleaving: B hace su `GetAsync` **antes** de que A escriba la reserva (ve `null`, así
que no compara); su `TryAcquireAsync` falla; vuelve a leer y para entonces A ya terminó
→ **B recibe la respuesta de A con un cuerpo distinto**, en silencio. Es exactamente la
falla que el 422 vino a cerrar, alcanzable por carrera en vez de en secuencia.

- [x] Reordenado a **reservar-primero**: la huella se compara **siempre** antes de
      reproducir. Con `SET NX GET` sale gratis, porque la entrada existente ya viene en
      la misma respuesta y no queda ningún camino que reproduzca sin haberla mirado.
      ⚠️ La revisión adversarial encontró un **segundo disparador** que yo no tenía: no
      hace falta simultaneidad. Bastaba con que el `GetAsync` inicial degradara por un
      timeout (devuelve `null`, se salta el 422) para que el segundo GET reprodujera el
      cuerpo equivocado. También queda cerrado: ya no hay dos lecturas.

---

## 16.3 🟠 La clave del cliente no tiene límite

Se aceptan claves de **7000 caracteres** (medido). La clave la elige el cliente y acaba
entera dentro de una clave de Redis que vive 24 h, en una instancia **compartida con
otros proyectos** y con `maxmemory 0` / `noeviction` (comprobado).

- [x] Se rechaza con **400** (`idempotency_key_invalid`) por encima de `MaxKeyLength`,
      **antes de ejecutar nada**. Por defecto **255**, que es lo que aceptan Stripe y el
      borrador de la IETF.

---

## 16.4 🟠 Los plazos son constantes, contra la regla §5 del propio proyecto

`ResponseTtl` (24 h) y `ReservationTtl` (60 s) son `private static readonly` en el
atributo. Además de saltarse la regla («toda sección se enlaza a una clase tipada
validada»), es lo que hace que la deuda de `planning/12` §12.5 —*la reserva es un lease
sin renovación*— **no se pueda probar**: no hay forma de bajar los 60 s desde el host de
tests.

- [x] `IdempotencyOptions` (sección `Idempotency`): `ResponseTtlHours`,
      `ReservationTtlSeconds`, `MaxKeyLength`. Con `ValidateDataAnnotations()` y
      `ValidateOnStart()`, como el resto. Se registra **fuera** del `if` de Redis: el
      filtro las lee aunque el store sea el Null Object, y una opción que sólo existe en
      una rama de registro es un fallo que aparece únicamente donde no hay
      infraestructura.
- [x] Test que **fija** el comportamiento del lease caducado (reserva de 1 s). No lo
      arregla: lo deja escrito, para que nadie lo descubra creyendo que es nuevo.

---

## 16.5 Tests — hoy hay 10, todos de integración y **ninguno unitario**

Huecos que se cierran (los demás quedan listados en §16.6):

- [x] `Idempotency-Replayed: true` **no se afirmaba en ningún test**, aunque el `.feature`
      la exige.
- [x] En la concurrencia, que sea **exactamente uno** el 200 y que el 409 traiga
      `code: idempotency_in_progress`.
- [ ] **422 en carrera** (dos cuerpos distintos simultáneos), no sólo en secuencia.
- [x] Aislamiento entre usuarios comprobando el **cuerpo**, no sólo el stock: la fuga
      entre cuentas se vería ahí.
- [ ] `ReleaseAsync` que lanza con Redis caído en el camino de **error** — es la rama que
      evita que un fallo de Redis sustituya al 409 real del dominio.
- [x] Longitud de clave → 400.
- [x] Caducidad de la reserva con TTL corto.

---

## 16.6 Queda abierto (decisión del owner o deuda anotada)

### ✅ RESUELTO por [`planning/17`](17_idempotencia-transaccional.md) — la pregunta estaba mal planteada

Lo que sigue quedó **medido y cerrado**, pero no eligiendo entre degradar o devolver 503:
moviendo la marca de idempotencia a la **misma transacción que el efecto**. Con eso, «el
almacén no está» y «la operación no puede ocurrir» son el mismo evento.

Estos eran los números **antes** de ese cambio, con carga sostenida sobre dos réplicas:

| | |
|---|---|
| Peticiones | 14 400 |
| `acquire` degradado (ejecutaba **sin garantía**) | **174 (1,21 %)** |
| `save` fallido | 0 |
| `release` fallido | 68 |

Bajar los viajes de 3 a 2 movió el umbral pero **no eliminó el modo de fallo**, y por eso
no se podía dar por resuelto aquí. Las tres salidas que se listaban —fallar en cerrado,
multiplexer propio, más conexiones— quedaron descartadas o irrelevantes: ver
`planning/17` §17.0 para el debate completo y §17.3 para qué cierra cada cosa.

### Deuda que sigue abierta

- [x] ✅ **Muerte del proceso entre el commit y el `SaveAsync`.** Cerrado en `planning/17`: ya no hay dos escrituras que sincronizar, es una. La compra ya está cobrada
      y la respuesta no se memorizó: el cliente recibe **409 «still being processed»**
      —que es mentira, no hay nadie procesando— hasta que caduque la reserva, y su
      siguiente reintento **vuelve a comprar**. Un deploy a media compra basta.
      Arreglarlo de verdad pide que la marca de idempotencia se escriba en la **misma
      transacción** que el efecto, o sea en SQL y no en Redis. Es el mismo razonamiento
      que ya se aplicó al outbox y al consumidor.
- [x] ✅ **El lease sin renovación.** Cerrado como riesgo en `planning/17`: que el
      marcador caduque solo hace que la duplicada pase la puerta y choque contra la clave
      primaria de `ExecutedCommands`. Ya no abre una ventana de doble ejecución.
- [ ] **Memorizar desde un `IAsyncResultFilter`** para poder capturar el `Location` de un
      201. Hoy no afecta —el único endpoint idempotente devuelve 200— pero el sitio donde
      se hace ahora **no puede** verlo: el `IActionResult` se ejecuta después de los
      filtros de acción. El código muerto ya se retiró y el porqué quedó escrito.
- [ ] **Huella vacía = 422 desactivado.** Si un argumento no serializa, `HashOf` devuelve
      cadena vacía y la comparación se salta entera. Con `BuyProductDto` no pasa; una
      acción con `IFormFile` lo activaría — y ahí, además, la huella tampoco incluiría
      los bytes del fichero.
- [ ] **Se libera la clave en caminos donde la operación SÍ ocurrió.** Si el `Commit`
      lanza después de que SQL Server lo aplicara (transacción *in doubt*), el cliente ve
      500, la clave se borra y el reintento vuelve a comprar.
- [x] ✅ **Identidad byte a byte del replay** (venía de `planning/12` §12.2). Cerrada
      como efecto secundario en `planning/17`: al memorizar el DTO y no la respuesta HTTP,
      el replay vuelve a pasar por el mismo formateador de MVC. Verificado 3/3.
- [ ] **`SqlException` escapando como 500 bajo carga.** En las pruebas: «Operation
      cancelled by user» (el cliente cuelga → EF cancela el `SqlCommand`) y Win32 258
      (timeout). Los primeros no son un fallo del servidor y ensucian las métricas de
      error. **Es adyacente a esta tarea, no parte de ella**: pide su propio planning.
- [x] ✅ **Redis compartido con `allkeys-lru`**: deja de importar para la corrección.
      Desalojar una clave solo pierde la puerta de admisión, no la garantía.

---

## 16.7 Verificación

- [x] `dotnet build` con **0 warnings** (la CI va con `-warnaserror`).
- [x] `dotnet test tests/ApiEcommerce.Tests`: **169 en verde** (eran 160; 9 nuevos).
- [x] Repetido **ejecutando**, con la API de verdad:
      ráfagas de 150/250/350 con la misma clave → baja 1, **4/4**;
      dos réplicas alternadas, 80 simultáneas → baja 1, **4/4**;
      viajes a Redis por petición **3,00 → 2,00**;
      contrato semántico completo, más el 400 por clave larga.
