# Lineamientos de arquitectura — ApiEcommerce

Arquitectura estilo **Spring Boot** sobre ASP.NET Core 9: DI por constructor,
repository + service detrás de interfaces, genéricos base que centralizan el
CRUD, y un handler global que mapea excepciones de dominio a códigos HTTP.

Estos documentos describen **el diseño objetivo**, no solo lo que hoy existe.
Donde el código discrepe, el documento manda y el código se migra siguiendo el
roadmap.

## Índice

| Doc | Léelo cuando… |
| --- | --- |
| [`00-arquitectura.md`](00-arquitectura.md) | quieras la visión general, el flujo de una petición y el mapa Spring ↔ .NET |
| [`01-capas-y-contratos.md`](01-capas-y-contratos.md) | dudes de en qué capa poner algo |
| [`02-repository.md`](02-repository.md) | toques `Repository/` o agregues una consulta |
| [`03-service.md`](03-service.md) | escribas lógica de negocio o un servicio nuevo (**empieza aquí para entender composición vs herencia**) |
| [`04-error-handling.md`](04-error-handling.md) | necesites devolver un error (400/401/403/404/409/500) |
| [`05-convenciones.md`](05-convenciones.md) | dudes de naming, estilo, DTOs, DI o migraciones |
| [`06-estado-y-roadmap.md`](06-estado-y-roadmap.md) | vayas a empezar a trabajar: qué está hecho y qué sigue |

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
8. **Todo lo nuevo se registra en DI**
   (`Shared/DependencyInjection/ServiceCollectionExtensions.cs`, lifetime `Scoped`).
9. **`Category` es el slice de referencia.** Ante la duda, copia su forma;
   `Product` es el ejemplo de una entidad con consultas y reglas propias.

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
