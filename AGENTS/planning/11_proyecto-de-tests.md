# 11 — Proyecto de tests `ApiEcommerce.Tests`  🟡 **EN CURSO** (fases 1–5 hechas; falta la 6, CI)

> **Por qué es el siguiente y no es opcional**: casi todos los bugs que encontró la revisión
> multiagente (`progress.md` §2) compilaban limpio y pasaban el smoke test manual. Solo
> aparecen con **concurrencia**, **fallo de una dependencia** o el entorno de **producción**.
> Hoy la única red es `dotnet build`.

**La especificación ya existe**: los `.feature` de `features/01`–`10`. Cada `Scenario`
debería poder convertirse en un test. Empezar por los que ya se verificaron a mano, para
que dejen de depender de que alguien se acuerde de correr `curl`.

> **Estado 2026-09-06**: fases 1 y 2 completas, **105 tests en verde**. Dos desvíos del
> plan, ambos deliberados:
> - El proyecto va en **`tests/ApiEcommerce.Tests`** (dentro del repo) y no en
>   `../ApiEcommerce.Tests`: la raíz del repo *es* la carpeta del proyecto, así que fuera
>   quedaría fuera de git. Obliga a excluir `tests/**` en el `.csproj` de la API.
> - **Sin FluentAssertions**: desde la v8 exige licencia comercial, y ya tenemos ese
>   problema abierto con AutoMapper. `Assert` de xunit basta.
> - Los paquetes de integración (`Mvc.Testing`, `Testcontainers.*`) se añadirán al empezar
>   la fase 3; hoy no hacen falta y serían peso muerto.

## Fase 1 — Andamiaje
- [x] `dotnet new xunit -o ../ApiEcommerce.Tests` + `dotnet add reference`
- [x] `dotnet sln add`; paquetes: `Moq`, `Microsoft.AspNetCore.Mvc.Testing`, `Testcontainers.MsSql`, `Testcontainers.Redis`
- [x] Estructura **espejo del slicing**: `Features/Catalog/`, `Features/Accounts/`, `Shared/`
- [x] Patrón AAA; un test por escenario del `.feature`, con el mismo nombre

## Fase 2 — Unitarios (sin base, sin Redis) — **empezar aquí**
Es donde vive la lógica y donde la composición abarató el testeo: las reglas se instancian
con un mock en una línea.
- [x] `CategoryRules` / `ProductRules` con `IXRepository` mockeado: un test por excepción de dominio
- [x] `CrudService` con `IBaseRepository<T>` + `IMapper` mockeados:
      404 en `GetByIdAsync`, reglas invocadas **antes** de escribir, update sobre la entidad rastreada
- [x] `AuthService` con `UserManager`/`SignInManager` mockeados:
      409 duplicado · 422 password débil · **401 con el MISMO mensaje** para usuario
      inexistente y contraseña mala · 403 lockout · **el registro nunca asigna `admin`**
- [x] `LocalFileStorage`: extensión no permitida, magic bytes que no casan, tamaño excedido,
      y que el nombre lo genere el servidor
- [x] `CachedCategoryService` con `ICacheService` falso: invalidación **después** de la
      escritura, y **no** invalidar si el servicio interno lanza
- [x] `PagedResult`: `TotalPages` con 0 elementos, `HasNext`/`HasPrevious` en los bordes
- [x] `GlobalExceptionHandler`: que encuentre el `SqlException` **desnudo** y el anidado
- [x] Perfiles de AutoMapper: `AssertConfigurationIsValid()` + PATCH parcial que no pisa campos

### Verificado por mutación (2026-09-06)
Un test que pasa contra el código roto no vale nada. Se reintrodujeron **dos bugs reales
ya corregidos** y la suite los cazó:
- quitar el `MapFrom` explícito de `CategoryId` → caen los 2 tests del PATCH parcial;
- volver `FindSqlException` a mirar solo el `InnerException` directo → caen los 2 del
  `SqlException` desnudo y el anidado en profundidad.

## Fase 3 — Integración (`WebApplicationFactory` + base de datos dedicada)
> ⚠️ **Sin Testcontainers**, a diferencia del plan: el entorno de trabajo es un dev
> container **sin Docker dentro**. Se usa la infraestructura ya levantada en el host, con
> una base propia (`ApiEcommerceNET8_Tests`, borrada y migrada en cada corrida) y un
> prefijo propio de Redis (`apiecommerce-tests:`). Testcontainers entra en la fase 6, donde
> el runner de CI sí tiene Docker y es donde de verdad aporta aislamiento.
- [x] SQL Server y Redis reales y efímeros por corrida
- [x] Autorización: la matriz completa de `features/02`
- [x] Versionado: `/api/category` → 404, `Location` con versión, `/health` neutral
- [x] Paginación: tope de `pageSize`, página fuera de rango → 200 con `[]`
- [x] Idempotencia: replay, concurrencia, liberación en error

## Fase 4 — Concurrencia (los que más valen)
⚠️ **Con `Task.WhenAll` de peticiones reales**, no secuencial: secuencialmente pasaban
también con la implementación defectuosa.
- [x] 15 compras simultáneas sobre stock 10 → 10×200, 5×409, stock 0
- [x] 8 POST simultáneos de la misma categoría → 1×201, 7×409, 1 fila
- [x] 6 compras concurrentes con la misma `Idempotency-Key` → una sola compra

## Fase 5 — Degradación y arranque
- [x] Con Redis caído: las lecturas responden y la compra no falla
- [x] Con el broker caído: la compra responde 200 y el evento queda en el outbox
- [x] Arranque en `Production` con `Seed:Enabled=false` y sin `AdminPassword` → arranca
- [x] Arranque sin `Jwt:SecretKey` → **falla**

### Lo que destaparon las fases 3–5 (2026-09-06)
Tres cosas que ni el build ni los 105 unitarios veían:
1. 🔴 **Los tests de idempotencia pasaban sin probar nada.** Con
   `ConfigureAppConfiguration`, el host arrancaba con `NoIdempotencyStore` y
   `NoCacheService`: un no-op no rompe una aserción de "dos operaciones distintas dan dos
   resultados", así que solo cayeron los dos tests que exigían un replay real. La causa es
   que `AddDistributedCaching` lee la configuración **eager** para decidir qué implementación
   registra, y eso ocurre **antes** de que se apliquen esos callbacks. Con `UseSetting` sí
   entra. Queda `TestHostGuardTests` como red permanente.
2. 🟠 **La degradación de Redis era correcta pero inservible.** Medido con Redis
   inalcanzable: un GET del catálogo tardaba **11 s** y una compra con `Idempotency-Key`
   **34 s**. Acotados los timeouts y **unificados los dos multiplexers** (el de
   `AddStackExchangeRedisCache` se quedaba con los de fábrica): 3 s y 7 s.
3. 🟠 **El P0 del crash-loop seguía vivo en otro atributo.** `[EmailAddress]` sobre
   `SeedOptions.AdminEmail` es incondicional igual que lo era el `[Required]` de
   `AdminPassword`: con el seeding apagado y `Seed__AdminEmail=` vacío, el arranque moría.
   Movido a `.Validate(...)`.

## Fase 6 — CI
- [ ] GitHub Actions: `dotnet build` + `dotnet test` en cada push
- [ ] Que el pipeline falle si algún test falla (obvio, pero hoy no existe pipeline)
