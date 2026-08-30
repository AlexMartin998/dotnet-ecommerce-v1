# 11 — Proyecto de tests `ApiEcommerce.Tests`  ❌ **SIGUIENTE**

> **Por qué es el siguiente y no es opcional**: casi todos los bugs que encontró la revisión
> multiagente (`progress.md` §2) compilaban limpio y pasaban el smoke test manual. Solo
> aparecen con **concurrencia**, **fallo de una dependencia** o el entorno de **producción**.
> Hoy la única red es `dotnet build`.

**La especificación ya existe**: los `.feature` de `features/01`–`10`. Cada `Scenario`
debería poder convertirse en un test. Empezar por los que ya se verificaron a mano, para
que dejen de depender de que alguien se acuerde de correr `curl`.

## Fase 1 — Andamiaje
- [ ] `dotnet new xunit -o ../ApiEcommerce.Tests` + `dotnet add reference`
- [ ] `dotnet sln add`; paquetes: `Moq`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`, `Testcontainers.Redis`
- [ ] Estructura **espejo del slicing**: `Features/Catalog/`, `Features/Accounts/`, `Shared/`
- [ ] Patrón AAA; un test por escenario del `.feature`, con el mismo nombre

## Fase 2 — Unitarios (sin base, sin Redis) — **empezar aquí**
Es donde vive la lógica y donde la composición abarató el testeo: las reglas se instancian
con un mock en una línea.
- [ ] `CategoryRules` / `ProductRules` con `IXRepository` mockeado: un test por excepción de dominio
- [ ] `CrudService` con `IBaseRepository<T>` + `IMapper` mockeados:
      404 en `GetByIdAsync`, reglas invocadas **antes** de escribir, update sobre la entidad rastreada
- [ ] `AuthService` con `UserManager`/`SignInManager` mockeados:
      409 duplicado · 422 password débil · **401 con el MISMO mensaje** para usuario
      inexistente y contraseña mala · 403 lockout · **el registro nunca asigna `admin`**
- [ ] `LocalFileStorage`: extensión no permitida, magic bytes que no casan, tamaño excedido,
      y que el nombre lo genere el servidor
- [ ] `CachedCategoryService` con `ICacheService` falso: invalidación **después** de la
      escritura, y **no** invalidar si el servicio interno lanza
- [ ] `PagedResult`: `TotalPages` con 0 elementos, `HasNext`/`HasPrevious` en los bordes
- [ ] `GlobalExceptionHandler`: que encuentre el `SqlException` **desnudo** y el anidado
- [ ] Perfiles de AutoMapper: `AssertConfigurationIsValid()` + PATCH parcial que no pisa campos

## Fase 3 — Integración (`WebApplicationFactory` + Testcontainers)
- [ ] SQL Server y Redis reales y efímeros por corrida
- [ ] Autorización: la matriz completa de `features/02`
- [ ] Versionado: `/api/category` → 404, `Location` con versión, `/health` neutral
- [ ] Paginación: tope de `pageSize`, página fuera de rango → 200 con `[]`
- [ ] Idempotencia: replay, concurrencia, liberación en error

## Fase 4 — Concurrencia (los que más valen)
⚠️ **Con `Task.WhenAll` de peticiones reales**, no secuencial: secuencialmente pasaban
también con la implementación defectuosa.
- [ ] 15 compras simultáneas sobre stock 10 → 10×200, 5×409, stock 0
- [ ] 8 POST simultáneos de la misma categoría → 1×201, 7×409, 1 fila
- [ ] 6 compras concurrentes con la misma `Idempotency-Key` → una sola compra

## Fase 5 — Degradación y arranque
- [ ] Con Redis caído: las lecturas responden y la compra no falla
- [ ] Con el broker caído: la compra responde 200 y el evento queda en el outbox
- [ ] Arranque en `Production` con `Seed:Enabled=false` y sin `AdminPassword` → arranca
- [ ] Arranque sin `Jwt:SecretKey` → **falla**

## Fase 6 — CI
- [ ] GitHub Actions: `dotnet build` + `dotnet test` en cada push
- [ ] Que el pipeline falle si algún test falla (obvio, pero hoy no existe pipeline)
