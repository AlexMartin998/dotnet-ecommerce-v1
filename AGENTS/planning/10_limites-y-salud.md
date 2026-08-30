# 10 — Límites de tasa, salud, observabilidad y despliegue  ✅

Contrato: [`features/10_limites-y-salud.feature`](../features/10_limites-y-salud.feature) · Commit `63269ac`

## Hecho
- [x] Rate limiting integrado en .NET 9 (cero paquetes): global por IP + política `auth`
- [x] `UseForwardedHeaders` — sin él, el limitador particiona por la IP del **proxy**
- [x] Serilog + `UseSerilogRequestLogging`
- [x] `/health` liveness (sin dependencias) y `/health/ready` (SQL + Redis + backlog)
- [x] `MigrateAsync()` al arrancar (la imagen de runtime no lleva `dotnet-ef`)
- [x] `Dockerfile` multi-stage, `$APP_UID`, `curl` instalado para el HEALTHCHECK
- [x] `docker-compose.fragment.yml` con `rabbitmq_generic` y secretos por `${VAR:?}`

## Decisiones
- **Dos políticas de rate limit**: el lockout de Identity cuenta fallos **por usuario**;
  sin límite por IP se sortea con password spraying contra muchos usuarios.
- **Liveness sin dependencias**: si dependiera de la base, una caída de la base haría que
  el orquestador reiniciara procesos sanos.
- **Serilog lee la sección `Serilog`**, no `Logging`. Se eliminó `Logging` para no tener
  dos fuentes de verdad de las que solo una funciona.

## Trampas registradas
- `curl` **no existe** en `mcr.microsoft.com/dotnet/aspnet:9.0` → el HEALTHCHECK dejaba el
  contenedor `unhealthy` para siempre.
- `[Required]` en `SeedOptions` reventaba el arranque con el seeding apagado (**crash-loop**).
- La configuración de .NET **fusiona arrays**: los orígenes CORS de dev seguían permitidos
  en producción.

## Abierto
- **Sin OpenTelemetry** (trazas y métricas) ni correlation id. Es lo primero que se pide en
  un incidente y no se añade después.
- **Sin CI**: nada corre `dotnet build` antes de un merge.
- El `Dockerfile` **no se ha construido nunca** (no hay Docker en el entorno).
- Falta `UseHsts()` en no-Development.
