# Lineamientos de arquitectura — ApiEcommerce

Arquitectura estilo **Spring Boot** sobre ASP.NET Core 9: DI por constructor,
repository + service detrás de interfaces, genéricos base que centralizan el
CRUD, y un handler global que mapea excepciones de dominio a códigos HTTP.

Estos documentos describen **el diseño objetivo**, no solo lo que hoy existe.
Donde el código discrepe en una **decisión de diseño**, el documento manda y el código se
migra siguiendo el roadmap.

⚠️ **Con un matiz que hay que tener presente**: hoy `01`–`05` van por detrás del código en
**rutas de carpeta, nombres de método de DI y conteos**. Ahí no manda el documento, manda
el código — y el documento se corrige. Lo que sigue siendo autoridad es su **razonamiento**,
que es lo que ningún documento regenerado reproduce. `00` y `06` están al día.

## Índice

| Doc | Léelo cuando… |
| --- | --- |
| [`00-arquitectura.md`](00-arquitectura.md) | quieras la visión general, el flujo de una petición y el mapa Spring ↔ .NET |
| [`01-capas-y-contratos.md`](01-capas-y-contratos.md) | dudes de en qué capa poner algo |
| [`02-repository.md`](02-repository.md) | toques un `Repository/` o agregues una consulta |
| [`03-service.md`](03-service.md) | escribas lógica de negocio o un servicio nuevo (**empieza aquí para entender composición vs herencia**) |
| [`04-error-handling.md`](04-error-handling.md) | necesites devolver un error (400/401/403/404/409/500) |
| [`05-convenciones.md`](05-convenciones.md) | dudes de naming, estilo, DTOs, DI o migraciones |
| [`06-estado-y-roadmap.md`](06-estado-y-roadmap.md) | vayas a empezar a trabajar: qué está hecho y qué sigue |

Lo que **no** tiene todavía documento propio aquí y hoy es la mitad de `Shared/`
—mensajería (outbox/inbox/RabbitMQ), idempotencia transaccional, documentos privados,
sesiones y refresh tokens, tests— está descrito en `CLAUDE.md` §7 y, con el detalle de por
qué se decidió cada cosa, en el `planning/` de la tarea que lo introdujo.

## Reglas que resumen todo

1. **Controller → Service → Repository → DbContext.** Nunca se salta una capa.
2. **El controller solo ve DTOs.** Las entidades no salen del servicio.
3. **El repositorio no lanza excepciones de negocio.** Devuelve `null`/`false`/vacío.
4. **El servicio lanza `AppException`.** No conoce HTTP.
5. **El handler global traduce la excepción al código HTTP.** Cero `try/catch`
   de negocio en controllers.
6. **El CRUD vive en los genéricos.** Las entidades específicas solo agregan lo
   que es propio de su dominio.
7. **Se hereda para reutilizar mecanismo, se compone para reutilizar política.**
   `BaseRepository<T>` se hereda; el CRUD de servicio (`ICrudService`) se compone,
   y las reglas de negocio viven fuera, en `IEntityRules`.
8. **Cada feature registra lo suyo en su propio `XxxExtensions.cs`.**
   `Shared/DependencyInjection/ServiceCollectionExtensions.cs` es el composition root y
   **no registra nada**: solo compone `AddSharedInfrastructure()` / `AddFeatures()` /
   `AddWebApi()`. `Scoped` para lo que depende de `AppDbContext`; **`Singleton`** para lo
   que no guarda estado por request (`IJwtTokenService`, `ICacheService`, `IFileStorage`,
   `IDocumentStore`, `IReceiptRenderer`), y **el mismo lifetime en todas las ramas**.
9. **`Catalog` es el slice de referencia y `Category` la entidad de referencia dentro de
   él.** Ante la duda, copia su forma; `Product` es el ejemplo de una entidad con consultas
   y reglas propias. ⚠️ Un slice es un **contexto acotado**, no una entidad: `Brand` o
   `ProductTag` van dentro de `Catalog/`, no crean carpeta.
10. **Un slice no toca los tipos de otro: habla con un puerto suyo.** `Ordering` no conoce
    `IProductRepository`, conoce `ICatalogGateway`, y toda la dependencia cruzada cabe en
    una clase adaptadora.

## Skill instalada

El repo tiene la skill [`dotnet-best-practices`](../../.agents/skills/dotnet-best-practices/SKILL.md)
(de `github/awesome-copilot`), instalada en `.agents/skills/` y enlazada a
`.claude/skills/`. Cubre buenas prácticas generales de .NET/C#: documentación
XML, async/await, DI, SOLID, manejo de errores.

**Precedencia:** cuando la skill y estos documentos discrepen, **manda esta
carpeta**. La skill es genérica y trae secciones que no aplican al proyecto
(Semantic Kernel, ResourceManager para localización, patrón Command Handler,
MSTest+FluentAssertions como framework obligatorio) — se ignoran. Lo que sí
aplica y refuerza los lineamientos: constructores primarios para DI, async/await
en toda I/O, interfaces con prefijo `I`, lifetimes explícitos, SOLID y evitar
duplicación.

Un matiz sobre esa skill: dice "evitar duplicación mediante **clases base**".
En este proyecto eso solo vale para `BaseRepository<T>`; en la capa de servicio la
reutilización es por **composición** (`03-service.md`), que es lo que de verdad
cumple SOLID —  responsabilidad única (las reglas son su propia clase), abierto/
cerrado (se extiende añadiendo reglas, no sobreescribiendo CRUD) e inversión de
dependencias (todo colaborador entra por el constructor detrás de una interfaz).
