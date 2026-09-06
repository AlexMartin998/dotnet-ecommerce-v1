# AGENTS/planning — Índice

Convención (`rules.md` §10): por tarea, un slug numerado `NN_<slug>`, con su contrato en
`AGENTS/features/NN_<slug>.feature` y su checklist técnico aquí.

| # | Tarea | Estado | Contrato |
|---|---|---|---|
| 01 | Auth: Identity + JWT | ✅ | `features/01_auth-y-registro.feature` |
| 02 | Autorización por roles | ✅ | `features/02_autorizacion-por-roles.feature` |
| 03 | Versionado de API + Swagger | ✅ | `features/03_versionado-de-api.feature` |
| 04 | Catálogo y paginación | ✅ | `features/04_catalogo-y-paginacion.feature` |
| 05 | Cache de catálogo (Redis) | ✅ | `features/05_cache-de-catalogo.feature` |
| 06 | Imágenes de producto | ✅ | `features/06_imagenes-de-producto.feature` |
| 07 | Compra y concurrencia | ✅ | `features/07_compra-y-concurrencia.feature` |
| 08 | Idempotencia | ✅ | `features/08_idempotencia.feature` |
| 09 | Eventos de dominio | ✅ | `features/09_eventos-de-dominio.feature` |
| 10 | Límites, salud y despliegue | ✅ | `features/10_limites-y-salud.feature` |
| 11 | **Proyecto de tests** | ✅ (262 tests + CI) | — (los `.feature` de 01–10 SON su especificación) |
| 12 | Deuda de la revisión multiagente | ✅ | §12.5 cerrada en `planning/18` |
| 13 | Refresh tokens y revocación | ✅ | `features/13_refresh-tokens.feature` |
| 14 | Administración de usuarios | ✅ | `features/14_admin-usuarios.feature` |
| 15 | Partir en proyectos | ❌ diferido | se escribirá al abordarla |
| 16 | Idempotencia bajo carga | ✅ | `features/16_idempotencia-bajo-carga.feature` |
| 17 | Idempotencia transaccional | ✅ | `features/17_idempotencia-transaccional.feature` |
| 18 | Deuda de mensajería | ✅ | `features/18_mensajeria-robusta.feature` |
| 19 | Errores bajo carga | ✅ | — (salió de una prueba de carga, no de un contrato) |
| 20 | **Órdenes y comprobante en PDF** | ✅ | `features/20_ordenes-y-comprobante.feature` |

Todos menos el **15** están hechos: el checklist queda como evidencia de qué se decidió y
qué quedó abierto. El 15 (partir en proyectos) sigue **diferido a propósito**, y la señal
para retomarlo está en [`docs/06`](../docs/06-estado-y-roadmap.md).

El `.feature` de una tarea se escribe **cuando se aborda**, no antes: un contrato Gherkin
escrito sin haber fijado el diseño acaba describiendo una solución imaginaria. Por eso el
19 no tiene ninguno — salió de una prueba de carga, no de un contrato.
