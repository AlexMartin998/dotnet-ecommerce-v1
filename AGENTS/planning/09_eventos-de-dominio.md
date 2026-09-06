# 09 — Eventos de dominio: outbox + RabbitMQ  ⚠️ PARCIAL

Contrato: [`features/09_eventos-de-dominio.feature`](../features/09_eventos-de-dominio.feature) · Commit `63269ac`

> ⚠️ **El camino del broker NUNCA se ha ejercitado**: no hay RabbitMQ ni Docker en el
> entorno de trabajo. Los escenarios `@broker @pendiente` del contrato están sin ejecutar.
> La única verificación de ese código es una revisión con `dotnet-best-practices`.

## Hecho
- [x] `OutboxMessage` + `ProcessedMessage` con índice filtrado sobre los pendientes
- [x] `IEventOutbox` / `EventOutbox` — escribe **sin** `SaveChanges` (manda la transacción de negocio)
- [x] `OutboxPublisher` (`BackgroundService`) con publisher confirms y mensajes persistentes
- [x] `RabbitMqConnection`: conexión perezosa, topología antes de publicar el campo, DLQ
- [x] `ProductPurchasedConsumer`: ack manual, prefetch, dedupe, DLQ
- [x] Sonda `outbox-backlog` → `Degraded`

## Decisiones
- **Outbox porque no se puede escribir en BD y broker atómicamente**: publicar antes del
  commit anuncia algo que no existe; publicar después lo pierde si falla.
- **Publicar → marcar procesado** (nunca al revés) ⇒ garantía **at-least-once** ⇒ el
  consumidor **tiene** que deduplicar.
- **La marca del consumidor se escribe ANTES del efecto**: al revés, la clave primaria solo
  arbitraría qué fila sobrevive y el efecto se habría aplicado dos veces.

## Verificado (sin broker)
Compras con el broker caído → 3×200, eventos persistidos, publicador reintentando.

## Pendiente — **ninguno**. Todo cerrado; se deja el rastro de dónde

- [x] **Levantar RabbitMQ** y ejecutar los `@broker`. → hecho el 2026-09-05 contra un
      broker real; esa verificación destapó un P0 que el build y el smoke test no veían.
- [x] Reintentos del consumidor con **contador real**. → [`12`](12_deuda-revision-multiagente.md)
      §12.1 (cola de espera con `x-message-ttl`), y luego
      [`18`](18_deuda-de-mensajeria.md) §18.2, que cambió `x-death` por una cabecera
      nuestra: la del broker sobrevive al paso por la DLQ y dejaba sin presupuesto a los
      mensajes reencolados por un operador.
- [x] **Claim en el outbox**. → [`12`](12_deuda-revision-multiagente.md) §12.1, resuelto con
      `sp_getapplock` exclusivo. Verificado con dos réplicas reales: 15 eventos, 7 y 8,
      cero duplicados.
- [x] **Purga** de `OutboxMessages` y `ProcessedMessages`. → `OutboxCleaner`
      ([`12`](12_deuda-revision-multiagente.md) §12.1). Desde
      [`17`](17_idempotencia-transaccional.md) purga también `ExecutedCommands`.
