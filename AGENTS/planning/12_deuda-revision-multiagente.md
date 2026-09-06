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
- [x] **`ETag` / `If-Match` para el *lost update* entre admins.** El GET publica el
      `rowversion` como `ETag`; el PATCH lee `If-Match` y `ProductRules` lo compara →
      **412** si el cliente leyó otra versión. **Opcional a propósito**: sin `If-Match` el
      PATCH sigue funcionando, porque exigirlo rompería a todos los clientes actuales.
      Token ilegible → **400**, no 500. El XML doc de `Product.RowVersion` actualizado, que
      era parte de la casilla.
- [x] **Hash del cuerpo en la clave de idempotencia.** SHA-256 de los argumentos ya
      enlazados (no del flujo crudo: cuando corre el filtro, el model binder ya lo consumió,
      y los argumentos además ignoran espacios y orden de campos). Se guarda **desde la
      reserva**, no al terminar: si no, una segunda petición con otro cuerpo que llegue
      *mientras la primera sigue en curso* no tendría contra qué comparar. Distinto → **422**.
      ⚠️ **Deuda que abre**: el replay re-serializa el cuerpo memorizado y no es idéntico
      byte a byte al vivo (`+` sale como `\u002B`). Equivalente para cualquier cliente que
      parsee JSON; byte-identidad exigiría capturar lo que MVC escribe.

## 12.3 Observabilidad y operación
- [x] **OpenTelemetry**: trazas (ASP.NET Core, HttpClient, SqlClient) y métricas
      (+ runtime), con `ParentBasedSampler` para no partir las trazas distribuidas, y
      `CorrelationIdMiddleware` que respeta el `X-Correlation-Id` entrante, lo devuelve y
      lo mete —junto al `TraceId`— en **todas** las líneas de log.
      ⚠️ **Se instrumenta siempre, se exporta solo si hay `OtlpEndpoint`**: misma decisión
      que con Redis y RabbitMQ, la observabilidad no puede ser el motivo de que la API no
      arranque. Y el **texto** de las consultas SQL NO se captura: llevaría los valores de
      los parámetros al backend de trazas.
- [x] **CI**: `.github/workflows/ci.yml` (build con `-warnaserror` + 153 tests en cada push y PR).
- [x] **Construir la imagen**: job `docker-build` en la CI.
- [x] `UseHsts()` en no-Development. (En Development no: la cabecera queda cacheada para `localhost` y rompe otros proyectos servidos en claro por ese host.)
- [x] Unificar las **tres** conexiones a Redis. Un solo multiplexer para
      `AddStackExchangeRedisCache` (vía `ConnectionMultiplexerFactory`), para el propio y
      para el health check. ⚠️ Con la cadena de conexión, la sonda abría la SUYA: podía
      decir `Healthy` con una conexión sana mientras la que sirve el tráfico estaba rota.

## 12.4 Decisión de producto pendiente
- [ ] **Licencia de AutoMapper.** La 15.1.1 exige licencia comercial en producción y lo avisa
      por log al arrancar. Opciones: comprar, fijar ≤13.x (última MIT), o migrar a Mapperly
      (source generator, MIT). Afecta a `docs/05-convenciones.md`.
