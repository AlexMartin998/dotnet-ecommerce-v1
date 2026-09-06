# 12 — Deuda abierta de la revisión multiagente (2026-08-30)  ❌

> Lo que se dejó **a propósito** sin cerrar, para que nadie lo descubra creyendo que es
> nuevo. Los P0 y los P1 baratos ya están corregidos (`progress.md` §2).

## 12.1 Mensajería
- [x] **Reintentos del consumidor con contador real.** Cola de espera
      `…product-purchased.retry` con `x-message-ttl`, que dead-letterea de vuelta a la
      principal; el contador sale de `x-death[].count` filtrando por esa cola.
      `MaxDeliveryAttempts` y `RetryDelaySeconds` vuelven a configuración ahora que hay
      algo real detrás.
      ⚠️ Se añade como topología **NUEVA** en vez de cambiar el `x-dead-letter-exchange` de
      la cola principal: redeclarar una cola existente con argumentos distintos da
      **406 PRECONDITION_FAILED** y obligaría a borrarla en producción, con sus mensajes.
      ⚠️ Y se publica al reintento **antes** de confirmar el original: al revés, morir
      entremedias pierde el mensaje.
      Verificado con un cuerpo ilegible y TTL de 2 s: intentos a los :34, :36 y :38 —el
      espaciado es el TTL— y al tercero a la DLQ.
- [x] **Claim en el `OutboxPublisher`.** Resuelto con **`sp_getapplock` exclusivo**
      (`@LockOwner='Transaction'`, `@LockTimeout=0`) en vez de un claim por filas.
      ⚠️ **La decisión importa**: un claim con `LockedUntil` permitiría a dos réplicas
      drenar *en paralelo*, y eso destruye la garantía de orden que da `Sequence`; además
      obliga a gestionar la expiración para que una réplica muerta no deje filas
      bloqueadas. Serializar el drenaje no cuesta nada (lote acotado, cada 5 s) y sale más
      simple **y** más correcto. Verificado: reteniendo el lock desde otra sesión, la
      compra sigue dando **200** y no se publica nada; al soltarlo, se publica.
- [x] **Purga.** `OutboxCleaner` (BackgroundService), retención configurable, borrado en
      tandas de 5.000 con `ExecuteDeleteAsync` para no escalar el bloqueo a toda la tabla.
      ⚠️ **Nunca toca lo no procesado**: un evento que agotó reintentos sigue pendiente de
      revisión y borrarlo sería perder el hecho de negocio en silencio. Verificado con una
      fila de 30 días agotada: sobrevive; la procesada de al lado se borra.
- [x] **Orden del outbox.** Columna `Sequence` (`bigint IDENTITY`): el orden lo asigna un
      único árbitro —el servidor SQL— y desempata las filas del mismo milisegundo. El
      índice filtrado de pendientes pasa a ir por `Sequence`.

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
- [x] **CI**: `.github/workflows/ci.yml` (build con `-warnaserror` + 153 tests en cada push y PR).
- [x] **Construir la imagen**: job `docker-build` en la CI.
- [ ] `UseHsts()` en no-Development.
- [~] Unificar las conexiones a Redis. **Hecho** para `AddStackExchangeRedisCache` y el
      `IConnectionMultiplexer` propio (un solo multiplexer vía `ConnectionMultiplexerFactory`);
      lo destapó medir la degradación: el de la cache se quedaba con los timeouts de
      fábrica y esperaba 5 s. **Falta** el health check.

## 12.4 Decisión de producto pendiente
- [ ] **Licencia de AutoMapper.** La 15.1.1 exige licencia comercial en producción y lo avisa
      por log al arrancar. Opciones: comprar, fijar ≤13.x (última MIT), o migrar a Mapperly
      (source generator, MIT). Afecta a `docs/05-convenciones.md`.
