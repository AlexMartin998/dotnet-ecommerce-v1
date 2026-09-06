# 17 — La idempotencia, de optimización a garantía

> Cierra la decisión que `planning/16` §16.6 dejó abierta. Spec:
> [`features/17`](../features/17_idempotencia-transaccional.feature).
>
> La pregunta era «¿fallamos en cerrado con 503 cuando Redis está vivo pero lento?».
> **La respuesta es que la pregunta estaba mal planteada.**

---

## 17.0 Cómo se decidió

Cuatro frentes en paralelo, porque la decisión no era técnica sino de diseño:

| Frente | Qué aportó |
|---|---|
| Abogado del **fail-closed** | La asimetría que nadie había visto: el fail-open se justificó por un incidente en `SaveAsync` (no devolver 500 por una compra ya cobrada), y ese argumento **no aplica al camino de reserva**, donde no hay nada que perder. Y los fallos medidos están **todos** ahí: 174 en el acquire, 0 en el save |
| Abogado del **fail-open** | Que `IsConnected` **no distingue** lo que se le pide distinguir (es `true` con Redis sano-ocioso y sano-saturado, y oscila en un incidente real con `AbortOnConnectFail=false`), que el patrón medido (2,6 % → 0 % → 22 %) es la firma de una **cola**, no de un fallo semántico, y que fallar en cerrado castiga justo al cliente que manda la cabecera |
| **Investigación** | Nadie en la industria ejecuta sin garantía. Adyen: 503 + `transient-error: true`, y la salida sin garantía la toma **el cliente** omitiendo la cabecera. AWS Powertools: falla cerrado por construcción. Y Azure documenta el patrón bueno: **marcador y efectos en la misma transacción, con una restricción de unicidad como árbitro**. Stripe guarda sus claves en su misma base de negocio |
| **DDD / Clean** | No hay que mover la idempotencia entera: hay que **partirla**. El protocolo (leer la cabecera, elegir el código) es adaptador; «este intento no se ejecuta dos veces» es invariante de negocio. Y un `catch` de un adaptador de salida no puede relajar una invariante |

**Los dos abogados llegaron a la misma conclusión por su cuenta**, y coincide con lo que
el repo ya hacía bien en `ProductPurchasedConsumer`: marca y efecto en una transacción.

---

## 17.1 Lo que se hizo

- [x] **`ExecutedCommands`** (`Shared/Idempotency/CommandIntent.cs`): tabla con el id del
      intento como **clave primaria**, la huella del comando y su resultado. Hermana de
      `ProcessedMessages`, y por la misma razón.
- [x] **`ICommandLog` / `CommandLog`**: el puerto, gemelo de `IEventOutbox`. Escribe en la
      unidad de trabajo del servicio y **no hace `SaveChanges`**: lo confirma la
      transacción de negocio, o no se confirma nada.
- [x] **`BuyAsync` recibe una `CommandIntent` sin valor por defecto.** El compilador
      obliga a decidir; renunciar se escribe (`CommandIntent.None`). Es la corrección del
      mismo defecto que motivó mover la transacción: una garantía que depende de que
      alguien se acuerde de un atributo se pierde en silencio en cuanto la llama un job.
- [x] **`CommandOutcome<T>`**: el servicio reporta «esto ya estaba ejecutado» como un
      **hecho de dominio**, y el controller lo traduce a `Idempotency-Replayed`. Sin esto
      la cabecera solo marcaba los replays del atajo, y una cabecera que *a veces* marca
      los replays es peor que ninguna.
- [x] **`IdempotencyConflictAppException`** (422, `idempotency_key_reuse`): la
      comprobación de la huella bajó a la transacción, así que tiene que poder expresarse
      sin conocer códigos de estado.
- [x] **`[Idempotent]` se queda en puerta de admisión.** Ya no memoriza respuestas: eso
      creaba una **segunda copia del cuerpo** que no coincidía byte a byte con la real.
      Sigue devolviendo 409 para duplicados en vuelo, que es lo que evita que 300
      reintentos se apilen bloqueados sobre la misma fila reteniendo conexiones.
- [x] **Purga** de `ExecutedCommands` en `OutboxCleaner`, con `Outbox:RetentionDays`.
- [x] **`rules.md` §8 reescrita**, §3 y §7 actualizadas.

---

## 17.2 Verificado ejecutando

Contra SQL Server, Redis 7.0.15 y RabbitMQ reales en `192.168.3.82`.

**Con Redis arriba:**

| | |
|---|---|
| Ráfagas de 150/250/350 con la misma clave | descuento **1**, 4/4 |
| Dos réplicas (80 simultáneas alternando) | descuento **1**, 4/4 |
| Contrato semántico (replay, 422, aislamiento, error no memorizado, 400 por clave larga) | correcto |
| Viajes a Redis por petición | **2,00** (entrar + soltar) |

**Con Redis apuntando a un puerto muerto** — lo que antes era imposible de cumplir:

| | |
|---|---|
| Reintento secuencial | descuento **3**, no 6 |
| **40 simultáneas** con la misma clave | descuento **1**, 3/3, y **todas 200** |
| Reuso con otro cuerpo | **422** `idempotency_key_reuse` |
| Identidad del replay | **byte a byte**, 3/3 |

⭐ **La identidad byte a byte se arregló sola.** Era deuda desde `planning/12` §12.2: el
filtro re-serializaba el cuerpo memorizado y el `+` del base64 salía como `+`, un
fallo intermitente y dependiente de los datos. Al memorizar el **DTO** en vez de la
respuesta HTTP, el replay vuelve a pasar por el mismo formateador de MVC.

- [x] `dotnet build -warnaserror` sin warnings · **171 tests** en verde.

---

## 17.3 Lo que esto cierra de `planning/16` §16.6

| Deuda | Estado |
|---|---|
| **Fail-open bajo carga** (174/14 400 sin garantía) | ✅ **Disuelto.** Lo que degrada es el atajo; la garantía está en la transacción |
| **Muerte del proceso entre el commit y el `SaveAsync`** | ✅ **Cerrado.** No hay dos escrituras que sincronizar: es una |
| **El lease sin renovación** | ✅ **Cerrado como riesgo.** Que el marcador caduque solo hace que la duplicada pase la puerta y choque contra la clave primaria |
| **Identidad byte a byte del replay** | ✅ **Cerrada** (efecto secundario) |
| **Token de propiedad de la reserva** | ✅ Sigue, ahora solo protege la puerta |
| **¿Fallar en cerrado con 503?** | ✅ **No hace falta.** Con la garantía en la base, ejecutar cuando Redis no responde ya no relaja nada |
| **Redis compartido con `allkeys-lru`** | ✅ **Deja de importar** para la corrección |
| `SqlException` escapando como 500 bajo carga | ✅ Cerrada en [`planning/19`](19_errores-bajo-carga.md) |

---

## 17.4 Lo que queda abierto

- [ ] **La huella se calcula sobre el DTO ya enlazado.** Dos cuerpos que difieren solo en
      espacios o en el orden de los campos son el mismo comando — que es lo que se quiere—
      pero un argumento no serializable (un `IFormFile`) daría una huella inútil. Hoy no
      hay ninguna operación así con intención declarada.
- [ ] **Solo `POST /product/buy` declara intención.** El mecanismo es general; aplicarlo a
      otra operación es añadir el parámetro y las dos líneas del `CommandLog`.
- [ ] **`ExecutedCommands` guarda el resultado en `nvarchar(max)`.** Suficiente para un
      DTO; si algún día se memoriza algo grande, conviene medirlo.
- [ ] **La retención vive en `Outbox:RetentionDays`**, que ahora gobierna tres tablas con
      criterios distintos. Cuando una necesite su propio plazo, hay que separarlas.
- [x] ✅ **`SqlException` escapando como 500 bajo carga** → **[`planning/19`](19_errores-bajo-carga.md)**:
      un cliente que cuelga se absorbe en `ClientAbortMiddleware` (499, Information, sin
      traza) y un timeout de base pasa a 503 con `Retry-After`. Medido: de 27 «unhandled
      exception» a 0.
