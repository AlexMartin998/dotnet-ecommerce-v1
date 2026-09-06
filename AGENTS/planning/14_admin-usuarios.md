# 14 — Administración de usuarios  ✅ **hecho** (2026-09-06)

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
- [x] **Un admin no puede quitarse a sí mismo el rol admin.** Es el clic con el que se deja
      fuera de su propio panel. 409, y verificado que sigue siendo admin después.
- [x] **No se puede quitar el rol al último administrador.**
      ⚠️ **Honestamente: hoy esta regla es inalcanzable por HTTP, y la anterior es la razón.**
      Para que el objetivo sea el último admin, o soy yo —y entonces manda la regla 1— o
      hay al menos dos (yo y él). No hay tercera opción. Se deja como defensa para cuando
      exista un endpoint que sí pueda llegar (borrar usuario, cambio de roles en lote), y
      queda dicho para que nadie la crea probada.
- [x] **Un admin no puede bloquearse a sí mismo** — no estaba en el plan y es la más fácil
      de olvidar: deja el panel inaccesible para su propio dueño.
- [x] `UserDto` **nunca** expone hash ni contraseña. El test lo afirma sobre el JSON crudo,
      no sobre el tipo: lo que hay que impedir es el campo añadido "por comodidad".
- [x] Toda promoción y toda revocación de rol se **audita** a nivel Warning con quién, a
      quién y cuándo. Sin rastro no hay forma de responder "¿quién le dio admin a este?"
      tres meses después.

## Checklist
- [x] [`features/14`](../features/14_admin-usuarios.feature)
- [x] `IUserAdminService` + `UserAdminService`
- [x] `UserController`, con `[Authorize(Roles = Roles.Admin)]` a nivel de clase
- [x] Registrado en `AccountsExtensions`
- [x] **9 tests**, centrados en las reglas duras

## 🔴 El agujero que esto destapó, y que era lo más importante de la tarea

Bloquear una cuenta **no servía de nada**. Identity marca el bloqueo y lo comprueba en el
*login*, pero:

- el usuario seguía dentro con el access token que ya tenía, y
- —lo grave— **podía seguir renovándolo indefinidamente**, porque renovar no vuelve a pedir
  credenciales y por tanto no pasaba por el bloqueo. Una cuenta "bloqueada" con una sesión
  abierta se quedaba dentro **para siempre**.

Cerrado por los dos lados, que es lo que hace que no dependa de acordarse de uno:

1. `LockAsync` **revoca todas las sesiones** del usuario (`RevokeAllSessionsAsync`).
2. `RotateAsync` comprueba `IsLockedOutAsync` antes de renovar — cubre además al usuario
   que se bloquea solo por fallar el login, donde nadie llama a `LockAsync`.

⚠️ Lo que **no** se puede cerrar: los access tokens ya emitidos. La denylist va por `jti` y
no sabemos cuáles son los de un usuario. Sobreviven **como mucho 15 minutos**, y en ese
rato ya no pueden renovar. Es consecuencia de que un JWT sea autocontenido.

## Verificación

- [x] `dotnet build -warnaserror` sin warnings · **202 tests** en verde (eran 193).
- [x] **Ejecutando**: listado paginado sin credenciales en el cuerpo; un usuario normal
      recibe 403; promover es idempotente (204/204); un rol inexistente da 400; quitarme mi
      propio admin da 409 **y sigo siendo admin**; bloquearme a mí mismo da 409; bloquear a
      otro le impide el login (403) y desbloquear se lo devuelve (200).

## Lo que queda abierto

- [ ] **Cambiar la contraseña no revoca las sesiones.** Debería —es la expectativa de
      cualquiera que cambia su contraseña porque sospecha— y ya es una sola llamada a
      `RevokeAllSessionsAsync`. Falta el endpoint de cambio de contraseña, que no existe.
- [ ] **No hay «cerrar sesión en todos mis dispositivos»** para el propio usuario. El
      mecanismo está entero; falta exponerlo en `auth`.
- [ ] **El listado hace una consulta de roles por usuario** (N+1). Se acepta: la página está
      acotada a 100 y es un panel de administración. Si molesta, la salida es un JOIN contra
      `UserRoles`, no subir el `pageSize`.
- [ ] **No se pueden borrar usuarios.** Deliberado por ahora: con pedidos y eventos
      apuntando a un usuario, borrar es una decisión de negocio (¿anonimizar? ¿archivar?)
      que no toca improvisar aquí.

## Nota
`ApplicationUser` **no** implementa `IEntity` (clave `string`), así que **no** entra en
`BaseRepository<T>` ni en `CrudService<>`. Esto se hace con `UserManager`, no con el CRUD
genérico — forzarlo dentro sería el error de meter una entidad en una abstracción que no le
sirve.
