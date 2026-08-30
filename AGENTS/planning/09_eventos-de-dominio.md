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

## Pendiente
- [ ] **Levantar RabbitMQ** con `docker-compose.fragment.yml` y ejecutar los `@broker`.
- [ ] Reintentos del consumidor con **contador real** (`args.Redelivered` es una bandera,
      no un contador: son 2 intentos sin backoff). Hace falta *retry queue* con
      `x-message-ttl` y leer `x-death[0].count`. → [`12`](12_deuda-revision-multiagente.md)
- [ ] **Claim en el outbox**: con más de una réplica, dos publicadores leen el mismo lote.
- [ ] **Purga** de `OutboxMessages` procesados y de `ProcessedMessages`.
