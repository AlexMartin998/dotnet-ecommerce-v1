# AGENTS/progress.md — Bitácora de avance y pendientes

> Qué está hecho, qué está a medias y qué falta, con la evidencia de cómo se verificó.
> **Se actualiza en el mismo commit que el código.** El diseño objetivo vive en
> `docs/06-estado-y-roadmap.md`; esto es la foto de ejecución.

Última actualización: **2026-09-06**.

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
| 11 | **Tests** | ❌ **siguiente** | [`planning/11`](planning/11_proyecto-de-tests.md) | — |
| 12 | Deuda de la revisión 2026-08-30 | ❌ | [`planning/12`](planning/12_deuda-revision-multiagente.md) | — |
| 13 | Refresh tokens y revocación | ❌ | [`planning/13`](planning/13_refresh-tokens.md) | — |
| 14 | Administración de usuarios | ❌ | [`planning/14`](planning/14_admin-usuarios.md) | — |
| 15 | Partir en proyectos | ❌ diferido | [`planning/15`](planning/15_partir-en-proyectos.md) | — |

**Ya no queda ningún ⚠️.** El 09 se cerró el 2026-09-05 contra un RabbitMQ real, y esa
verificación destapó un bug que el build y el smoke test no veían (abajo).

---

## 2. Bitácora

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

**No verificado**: el `Dockerfile` construido y `docker-compose.prod.yml` levantado (no hay
Docker en el dev container), y **nada sobre el runtime .NET 9**: todo lo de arriba corrió
sobre .NET 10 con `DOTNET_ROLL_FORWARD=Major`.

---

## 4. Pendientes, en orden

1. **Tests** ([`planning/11`](planning/11_proyecto-de-tests.md)) — el siguiente y no es
   opcional: casi todos los bugs de la revisión solo aparecen con concurrencia, fallo de
   dependencia o el entorno de producción. Los `.feature` de `features/` **son** la
   especificación.
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
| **Secretos de desarrollo** | `appsettings.Development.json` está commiteado con la clave JWT y la password de SQL. Aceptable en local; hay que moverlo a user-secrets antes de que el repo salga de la máquina. |
| **14 eventos enterrados** | Los abandonó el bug de reintentos, no son mensajes malos. ¿Se republican (`UPDATE OutboxMessages SET Attempts = 0 WHERE ProcessedAt IS NULL AND Attempts >= 5`) o se descartan? Hasta entonces `/health/ready` = `Degraded`. |
| **Runtime .NET 9** | El dev container ya solo trae .NET 10 y el proyecto es `net9.0`. ¿Instalar el runtime 9 en el contenedor, o **migrar el proyecto a `net10.0`** (con EF Core 10 y el `Dockerfile` a `aspnet:10.0`)? Mientras tanto solo arranca con `DOTNET_ROLL_FORWARD=Major`. |
