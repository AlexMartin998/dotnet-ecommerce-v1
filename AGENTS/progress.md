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
| 11 | **Tests** | ✅ fases 1–6 (**153 tests** + CI) | [`planning/11`](planning/11_proyecto-de-tests.md) | `14c9e76` + |
| 12 | Deuda de la revisión 2026-08-30 | ❌ | [`planning/12`](planning/12_deuda-revision-multiagente.md) | — |
| 13 | Refresh tokens y revocación | ❌ | [`planning/13`](planning/13_refresh-tokens.md) | — |
| 14 | Administración de usuarios | ❌ | [`planning/14`](planning/14_admin-usuarios.md) | — |
| 15 | Partir en proyectos | ❌ diferido | [`planning/15`](planning/15_partir-en-proyectos.md) | — |

**Ya no queda ningún ⚠️.** El 09 se cerró el 2026-09-05 contra un RabbitMQ real, y esa
verificación destapó un bug que el build y el smoke test no veían (abajo).

---

## 2. Bitácora

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
