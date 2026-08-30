# 14 — Administración de usuarios  ❌

## Problema
El **único** camino para tener un administrador es el `DataSeeder`. No hay forma de listar
usuarios, promover a admin, bloquear o desbloquear una cuenta.

## Diseño propuesto
Slice `Accounts` (no crea contexto nuevo: es su mismo lenguaje).

- `GET /api/v1/user` — listado **paginado** (reusar `PagedResult` / `PageQuery`), admin
- `GET /api/v1/user/{id}` — detalle, admin
- `POST /api/v1/user/{id}/roles` — asignar rol, admin
- `DELETE /api/v1/user/{id}/roles/{role}` — quitar rol, admin
- `POST /api/v1/user/{id}/lock` / `unlock` — admin

## Reglas duras
- [ ] **Un admin no puede quitarse a sí mismo el rol admin** (se quedaría el sistema sin admins).
- [ ] **No se puede borrar el último administrador.**
- [ ] `UserDto` **nunca** expone hash ni contraseña (ya se cumple).
- [ ] Toda promoción a admin se **audita** (quién, a quién, cuándo).

## Checklist
- [ ] Escribir `features/14_admin-usuarios.feature`
- [ ] `IUserAdminService` en `Features/Accounts/Service/`
- [ ] `UserController` en `Features/Accounts/Controllers/`
- [ ] Registrar en `AccountsExtensions.AddAccountsFeature()`
- [ ] Tests de las reglas duras (son justo las que un descuido rompe)

## Nota
`ApplicationUser` **no** implementa `IEntity` (clave `string`), así que **no** entra en
`BaseRepository<T>` ni en `CrudService<>`. Esto se hace con `UserManager`, no con el CRUD
genérico — forzarlo dentro sería el error de meter una entidad en una abstracción que no le
sirve.
