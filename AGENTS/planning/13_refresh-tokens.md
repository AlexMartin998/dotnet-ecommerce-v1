# 13 — Refresh tokens y revocación  ❌

## Problema
El access token dura 60 min y **no se puede revocar**. Un token robado vale hasta que
expira; cambiar la contraseña no invalida los tokens ya emitidos.

## Diseño propuesto (a fijar antes de escribir el `.feature`)
- `RefreshToken` como entidad del slice `Accounts`: `Token` (hash), `UserId`, `ExpiresAt`,
  `RevokedAt`, `ReplacedByToken`, `CreatedByIp`.
- **Rotación**: cada uso emite uno nuevo e invalida el anterior. Reusar uno ya rotado es
  señal de robo → revocar toda la familia.
- **Denylist de `jti` en Redis** para el access token: el claim **ya se emite**, y la
  conexión a Redis ya existe. TTL = lo que le quede de vida al token.
- El refresh token viaja en **cookie `HttpOnly` + `Secure` + `SameSite=Strict`**, no en el
  cuerpo: es el que de verdad hay que proteger de XSS.

## Checklist
- [ ] Fijar el diseño con el owner y escribir `features/13_refresh-tokens.feature`
- [ ] Entidad + migración
- [ ] `POST /api/v1/auth/refresh` y `POST /api/v1/auth/logout`
- [ ] Revocar familia al detectar reuso
- [ ] Denylist de `jti` consultada en la validación del token
- [ ] Bajar `Jwt:ExpirationMinutes` (con refresh, 15 min sobra)
- [ ] Purga de refresh tokens expirados

## Dependencias
Redis ya está. Conviene tener [`11`](11_proyecto-de-tests.md) antes: esto toca el camino de
autenticación entero y no hay red de seguridad.
