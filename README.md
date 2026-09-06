# ApiEcommerce

API REST de e-commerce en **ASP.NET Core 9 + EF Core 9 + SQL Server**, con **Redis** y
**RabbitMQ**. Backend-only.

Es un **proyecto de aprendizaje con estándar de producción**: el autor viene de Spring Boot
y usa el repo para aprender .NET replicando una arquitectura que ya domina. Por eso el
código lleva escrito **el porqué de cada decisión y la alternativa que se descartó**, casi
siempre con la medición que la motivó.

---

## Qué trae

**Dominio** — catálogo (categorías y productos), identidad y sesiones, y órdenes con
comprobante en PDF. Tres contextos acotados en **vertical slicing**.

**Y lo que lo hace prod-ready**, que es donde está el trabajo de verdad:

| | |
|---|---|
| **Concurrencia** | UPDATE condicional atómico para el stock, concurrencia optimista (`RowVersion` + `ETag`/`If-Match`) para editar, índices únicos en la base para la unicidad |
| **Idempotencia transaccional** | la marca del comando se escribe en la **misma transacción** que el efecto (`ExecutedCommands`); Redis queda como atajo para duplicados en vuelo, no como garantía |
| **Mensajería** | **outbox** transaccional → RabbitMQ con *publisher confirms* → **inbox** (exactamente una vez) → reintentos con espera → DLQ por cola |
| **Sesiones revocables** | JWT corto + refresh token en cookie `HttpOnly`, con rotación, detección de reuso y revocación por familia |
| **Almacenamiento sustituible** | imágenes públicas y documentos privados detrás de puertos distintos: cambiar a S3/R2/MinIO es una implementación y una línea de DI |
| **Errores** | jerarquía de dominio → RFC 7807 `ProblemDetails`, con `code` estable y `correlationId` |
| **Operación** | health checks con degradación explícita, rate limiting, Serilog + OpenTelemetry, Docker multi-stage, CI con `-warnaserror` |
| **Tests** | unitarios + integración contra SQL Server y Redis **reales**, incluidos los de concurrencia, degradación y arranque |

Ninguna de esas piezas está por completar el checklist: cada una nació de un bug medido, y
el porqué está en `notes.md` y en `AGENTS/planning/`.

## Puesta en marcha

```sh
dotnet restore
dotnet build
dotnet watch run --urls "http://0.0.0.0:8021"
```

⚠️ **No arranca sin user-secrets.** El repo no lleva ningún secreto, tampoco los de
desarrollo: hay que poner `ConnectionStrings:ConexionSql`, `Jwt:SecretKey` y
`Seed:AdminPassword`. Los comandos exactos y la puesta a punto de la infraestructura están
en **[`README_init.md`](README_init.md)**.

- **Swagger** (solo en Development): `/swagger/index.html`, un documento por versión.
- **Sondas**: `GET /health` (liveness) y `GET /health/ready` (SQL + Redis + backlog del
  outbox).
- **Tests**: `dotnet test tests/ApiEcommerce.Tests` (necesita SQL Server y Redis arriba).
- **Migraciones**: `dotnet ef migrations add <Nombre>` / `dotnet ef database update`.

La infraestructura (SQL Server, Redis, RabbitMQ) vive **fuera de este repo**, en un
`docker-compose` compartido; aquí solo está el bloque a pegar
(`docker-compose.fragment.yml`) y el despliegue de la propia app (`Dockerfile`,
`docker-compose.prod.yml`).

## Cómo está organizado

```
Features/          un contexto acotado por carpeta: Catalog, Accounts, Ordering
Shared/            transversal: persistencia, CRUD compuesto, mensajeria, idempotencia,
                   cache, almacenamiento, HTTP... y el composition root
Data/              AppDbContext + seeding
Exceptions/        jerarquia AppException -> HTTP
tests/             xunit + Moq, estructura espejo del slicing
```

La regla que lo sostiene: **se hereda para reutilizar mecanismo, se compone para reutilizar
política.** El repositorio base se hereda; el CRUD de servicio se compone y las reglas de
negocio viven fuera, en su propia clase.

## Documentación

| Dónde | Qué |
|---|---|
| [`CLAUDE.md`](CLAUDE.md) | la guía completa: arquitectura, garantías, convenciones y las trampas ya pisadas |
| [`AGENTS/docs/`](AGENTS/docs/) | los lineamientos de arquitectura, con el razonamiento largo |
| [`AGENTS/rules.md`](AGENTS/rules.md) | las reglas duras del repo |
| [`AGENTS/planning/`](AGENTS/planning/) | qué se decidió en cada tarea y qué quedó abierto |
| [`notes.md`](notes.md) | el log de aprendizaje: un capítulo por feature, con los gotchas |
| [`README_init.md`](README_init.md) | el cheatsheet de scaffolding desde cero |
