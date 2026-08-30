# 12 — Deuda abierta de la revisión multiagente (2026-08-30)  ❌

> Lo que se dejó **a propósito** sin cerrar, para que nadie lo descubra creyendo que es
> nuevo. Los P0 y los P1 baratos ya están corregidos (`progress.md` §2).

## 12.1 Mensajería
- [ ] **Reintentos del consumidor con contador real.** `args.Redelivered` es una bandera del
      broker, no un contador: efectivamente son 2 intentos y con 0 ms entre ellos (un
      requeue devuelve el mensaje a la **cabeza** de la cola).
      → *retry queue* con `x-message-ttl` que dead-letterea de vuelta a la principal, y
      leer `x-death[0].count`. Se quitó `MaxDeliveryAttempts` de la configuración por no
      dejar una opción muerta que documenta algo que no ocurre.
- [ ] **Claim en el `OutboxPublisher`.** El `SELECT` no tiene `UPDLOCK`/`READPAST` ni columna
      de reserva: con dos réplicas, ambas leen el mismo lote y publican duplicados. No
      corrompe (el consumidor deduplica) pero dobla el tráfico.
      → `sp_getapplock`, o `UPDATE TOP(n) ... OUTPUT` que reserve el lote.
- [ ] **Purga.** `OutboxMessages` procesados y `ProcessedMessages` crecen sin límite.
- [ ] **Orden del outbox.** Se ordena por `OccurredAt` (`DateTime.Now`, local) **sin
      desempate**: entre réplicas depende del reloj de cada máquina.

## 12.2 Concurrencia y contrato
- [ ] **`ETag` / `If-Match` para el *lost update* entre admins.** `RowVersion` existe pero no
      se expone en `ProductDto` ni se acepta en `UpdateProductDto`, así que el PATCH usa el
      token que acaba de leer. El XML doc de `Product.RowVersion` ya dice qué garantiza y
      qué no — **si se cierra, hay que actualizarlo**.
- [ ] **Hash del cuerpo en la clave de idempotencia.** Hoy la misma clave con otro payload
      reproduce la respuesta del primero en silencio. Lo estándar es 422 si no coincide.

## 12.3 Observabilidad y operación
- [ ] **OpenTelemetry**: trazas y métricas, más un correlation id por request. Es lo primero
      que se pide en un incidente y no se puede añadir *después* del incidente.
- [ ] **CI**: nada corre `dotnet build` antes de un merge.
- [ ] **Construir la imagen**: el `Dockerfile` nunca se ha construido (no hay Docker aquí).
- [ ] `UseHsts()` en no-Development.
- [ ] Unificar las **tres** conexiones a Redis (`AddStackExchangeRedisCache`, el
      `IConnectionMultiplexer` propio, y el health check) pasando el multiplexer a las otras.

## 12.4 Decisión de producto pendiente
- [ ] **Licencia de AutoMapper.** La 15.1.1 exige licencia comercial en producción y lo avisa
      por log al arrancar. Opciones: comprar, fijar ≤13.x (última MIT), o migrar a Mapperly
      (source generator, MIT). Afecta a `docs/05-convenciones.md`.
