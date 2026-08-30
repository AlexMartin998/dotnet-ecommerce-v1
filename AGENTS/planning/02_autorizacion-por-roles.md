# 02 — Autorización por roles  ✅

Contrato: [`features/02_autorizacion-por-roles.feature`](../features/02_autorizacion-por-roles.feature) · Commit `34a3b44`

## Hecho
- [x] `Shared/Auth/Roles.cs` con `const string` (un atributo solo admite constantes de compilación)
- [x] `[Authorize]` a nivel de clase + `[AllowAnonymous]` en lecturas + `[Authorize(Roles = Admin)]` en escrituras
- [x] La compra solo exige estar autenticado

## Decisión que costó un bug
⚠️ **Varios `[Authorize]` se COMBINAN (AND), no se sobreescriben.** Con
`[Authorize(Roles="admin")]` en la clase, un `[Authorize]` en la acción **no relaja nada**:
`POST /product/buy` daba 403 a un usuario normal. Solo `[AllowAnonymous]` gana sobre la
clase. Por eso el requisito **débil** va en la clase y el fuerte en cada acción — y así un
endpoint nuevo sin atributo queda protegido, no abierto.

## Abierto
- No hay autorización basada en propiedad del recurso (nadie "posee" nada todavía).
- Endpoint para promover a admin → [`14`](14_admin-usuarios.md). Hoy solo el seeder crea admins.
