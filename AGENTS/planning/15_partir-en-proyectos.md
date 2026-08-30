# 15 — Partir en proyectos  ❌ **DIFERIDO A PROPÓSITO**

> **No hacerlo todavía.** Hacerlo antes de que el proyecto lo pida solo añade fricción.
> Esto es el registro de cuándo tocará y por dónde se corta.

## Señal para empezar
Cuando quieras que **el compilador** —y no una convención— impida que la capa de aplicación
vea la infraestructura. Hoy la dirección `Web → Features → Shared` es una convención
declarada en el composition root.

Otras señales, de `docs/06-estado-y-roadmap.md`:

| Señal | A dónde lleva |
|---|---|
| Un caso de uso orquesta 3+ agregados y las reglas ya no caben en `IEntityRules` | use cases / handlers |
| Lecturas y escrituras divergen tanto que el mismo modelo estorba | CQRS |
| Invariantes que cruzan entidades (un `Order` cuyas líneas deben cuadrar con el total) | agregados DDD |

**Ninguna se cumple hoy.**

## Por dónde se corta
Las costuras **ya existen** y son los tres bloques del composition root:

```
ApiEcommerce.Api             -> AddWebApi              controllers, versionado, CORS, errores
ApiEcommerce.Infrastructure  -> AddSharedInfrastructure EF Core, Redis, disco, mensajería
ApiEcommerce.Application     -> AddFeatures             servicios, reglas, DTOs
ApiEcommerce.Domain          -> entidades e interfaces de repositorio
```

El vertical slicing por contexto acotado ayuda aquí: `Features/Catalog/` ya tiene todo
junto, así que mover un contexto entero es mover una carpeta.

## Checklist (cuando toque)
- [ ] Confirmar que se cumple al menos una señal
- [ ] `Domain`: entidades + `IEntity`/`IAuditable` + interfaces de repositorio
- [ ] `Application`: servicios, reglas, DTOs, `ICrudService` (referencia solo a `Domain`)
- [ ] `Infrastructure`: `AppDbContext`, repositorios, Redis, storage, mensajería
- [ ] `Api`: controllers, `Program.cs`, composition root
- [ ] Verificar que `Application` **no compila** si referencia `Infrastructure` — ese es
      todo el objetivo del ejercicio
- [ ] Mover los tests a la misma estructura
