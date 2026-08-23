# Reglas del repo

Los lineamientos de arquitectura viven en [`AGENTS/docs/`](docs/README.md).
**Léelos antes de escribir código**; el índice dice qué documento aplica a qué
tarea.

Resumen operativo:

- Flujo: **Controller → Service → Repository → AppDbContext**. No saltar capas.
- Controllers hablan **solo DTOs**; repositorios hablan **solo entidades**.
- El servicio lanza `AppException`; el handler global la traduce a HTTP.
- Sin `try/catch` de negocio en controllers.
- CRUD en los genéricos base (`BaseRepository<T>`, `BaseService<...>`).
- Todo lo nuevo se registra en `Program.cs` con lifetime `Scoped`.
- Verificación: `dotnet build` (no hay tests ni linter).
- Comentarios en español. **No borrar los bloques comentados** de
  implementaciones anteriores: son registro de aprendizaje del autor.
