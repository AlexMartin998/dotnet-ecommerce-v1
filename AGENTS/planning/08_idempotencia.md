# 08 — Idempotencia de peticiones  ✅

Contrato: [`features/08_idempotencia.feature`](../features/08_idempotencia.feature) · Commit `63269ac`

## Hecho
- [x] `Shared/Idempotency/`: `IIdempotencyStore`, `RedisIdempotencyStore`, `NoIdempotencyStore`, `IdempotentAttribute`
- [x] `SET NX` atómico vía `IConnectionMultiplexer`
- [x] TTL **separados**: 60 s la reserva, 24 h la respuesta
- [x] Clave = usuario + método + ruta + querystring + clave del cliente
- [x] Replay conserva cabeceras (incluido `Location`)
- [x] `IOrderedFilter` para quedar **por fuera** de `[Transactional]`
- [x] Aplicado a `POST /api/v1/product/buy`

## Decisiones
- **`IConnectionMultiplexer` y no `IDistributedCache`**: esa abstracción no tiene
  "set si no existe", que es justo la primitiva necesaria.
- **Sin usuario no se aplica idempotencia**: un espacio `"anonymous"` compartido haría que
  dos clientes se reprodujeran la respuesta el uno al otro.
- **Solo se memoriza el éxito**: memorizar un error lo volvería permanente 24 h.
- **Falla en abierto** (ver [`features/08`](../features/08_idempotencia.feature)).

## Medido
6 compras concurrentes de 3 uds con la misma clave → 1×200 + 5×409, **una sola compra**.
5 reintentos secuenciales → 4 replays, stock intacto.

## Abierto
- **No se guarda un hash del cuerpo**: la misma clave con otro payload reproduce la
  respuesta del primero en silencio. Lo estándar (Stripe) es 422 si no coincide.
- Sin tope de tamaño para el cuerpo que se guarda en Redis 24 h.
