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
| 09 | Eventos de dominio | ⚠️ parcial | `features/09_eventos-de-dominio.feature` |
| 10 | Límites, salud y despliegue | ✅ | `features/10_limites-y-salud.feature` |
| 11 | **Proyecto de tests** | ❌ siguiente | — (los `.feature` de 01–10 SON su especificación) |
| 12 | Deuda de la revisión multiagente | ❌ | se escribirá al abordarla |
| 13 | Refresh tokens y revocación | ❌ | se escribirá al abordarla |
| 14 | Administración de usuarios | ❌ | se escribirá al abordarla |
| 15 | Partir en proyectos | ❌ diferido | se escribirá al abordarla |

**01–10** son registro: la tarea está hecha y el checklist queda como evidencia de qué se
decidió y qué quedó abierto. **11–15** son plan ejecutable.

Los `.feature` de 11–15 se escriben **cuando se aborde la tarea**, no antes: un contrato
Gherkin escrito sin haber fijado el diseño acaba describiendo una solución imaginaria.
