# 01 — Auth: Identity + JWT  ✅

Contrato: [`features/01_auth-y-registro.feature`](../features/01_auth-y-registro.feature) · Commit `34a3b44`

## Hecho
- [x] `ApplicationUser : IdentityUser`; `AppDbContext : IdentityDbContext<ApplicationUser>`
- [x] `AddIdentityCore` (**no** `AddIdentity`: API stateless, los esquemas de cookie estorban)
- [x] Política de contraseñas y lockout explícitos (los defaults implícitos no los conoce nadie)
- [x] `JwtOptions` tipado con `ValidateOnStart`
- [x] `ConfigureJwtBearerOptions` (`IConfigureNamedOptions`) con las **cuatro** validaciones activas
- [x] `IJwtTokenService` separado del `AuthService` (singleton, materializa la clave una vez)
- [x] `AuthController`: `register` / `login` / `me`
- [x] Migración `AddIdentitySupport` (7 tablas `AspNet*`)

## Decisiones
- **El rol NO viaja en el body del registro.** En el curso, `POST /Users` era anónimo y
  aceptaba `"Role": "Admin"` → escalada de privilegios trivial.
- **Mismo mensaje** para usuario inexistente y contraseña incorrecta (evita enumeración).
- `exp`/`nbf` en UTC (lo exige RFC 7519) aunque el resto del modelo use hora local.
- Un claim `role` **por rol**: el curso mandaba solo `roles.FirstOrDefault()`.

## Abierto
- Refresh tokens y revocación → [`13`](13_refresh-tokens.md). El claim `jti` ya se emite.
- Confirmación de email y reset de contraseña: `AddDefaultTokenProviders()` está, sin usar.
