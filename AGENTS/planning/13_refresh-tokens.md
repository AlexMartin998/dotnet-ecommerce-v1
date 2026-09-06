# 13 — Refresh tokens y revocación  ✅ **hecho** (2026-09-06)

## Problema
El access token dura 60 min y **no se puede revocar**. Un token robado vale hasta que
expira; cambiar la contraseña no invalida los tokens ya emitidos.

## Diseño, fijado con el owner el 2026-09-06
- `RefreshToken` como entidad del slice `Accounts`: `Token` (hash), `UserId`, `ExpiresAt`,
  `RevokedAt`, `ReplacedByToken`, `CreatedByIp`.
- **Rotación**: cada uso emite uno nuevo e invalida el anterior. Reusar uno ya rotado es
  señal de robo → revocar toda la familia.
- **Denylist de `jti` en Redis** para el access token: el claim **ya se emite**, y la
  conexión a Redis ya existe. TTL = lo que le quede de vida al token.
- El refresh token viaja en **cookie `HttpOnly` + `Secure` + `SameSite=Strict`**, no en el
  cuerpo: es el que de verdad hay que proteger de XSS. **Elegido por el owner**, sabiendo
  el precio: exige CORS con credenciales, obliga a HTTPS y complica a un cliente móvil.

## Lo que el diseño propuesto NO decía, y hubo que decidir

- **Familia por sesión (`FamilyId`)** en vez de seguir el rastro de `ReplacedByToken`.
  Recorrer la cadena exige una consulta por eslabón y basta con que falte uno para dejar
  media sesión viva. Con la familia, revocar es un solo `UPDATE`.
- ⚠️ **Ventana de gracia para el reuso.** Sin ella la detección de robo es **inutilizable**:
  un móvil o una SPA lanzan varias peticiones a la vez, dos reciben 401 casi a la vez, las
  dos refrescan con el mismo token y la segunda parece un ladrón. Se cerraría la sesión de
  usuarios legítimos constantemente. Dentro de la ventana se rechaza igual (ese token está
  gastado) pero **no** se revoca la familia. Es el *leeway* de Auth0.
- **`TryConsumeAsync` es un `UPDATE … WHERE RevokedAt IS NULL`**, no un leer-y-escribir:
  entre comprobar y marcar cabe otra petición, y entonces dos rotarían el mismo token. Es
  la misma razón por la que el stock se descuenta con un UPDATE condicional.
- **SHA-256 a secas** para la huella, no un hash de contraseña con sal: el valor ya son 256
  bits aleatorios de un CSPRNG, así que no hay nada que adivinar por fuerza bruta y un
  algoritmo lento solo añadiría latencia a cada refresh.
- **`logout` es `[AllowAnonymous]`**: el caso más habitual de cerrar sesión es que el
  access token ya haya expirado. Exigir uno válido dejaría al usuario sin poder salir justo
  cuando más falta le hace.

## Checklist
- [x] Diseño fijado con el owner · [`features/13`](../features/13_refresh-tokens.feature)
- [x] Entidad `RefreshToken` + migración (`CREATE TABLE` limpio, revisada antes de aplicar)
- [x] `POST /api/v1/auth/refresh` y `POST /api/v1/auth/logout`; `login` y `register` abren sesión
- [x] Revocar la familia al detectar reuso, con ventana de gracia
- [x] Denylist de `jti` en `OnTokenValidated`
- [x] `Jwt:ExpirationMinutes` de 60 a **15**
- [x] Purga: `RefreshTokenCleaner`, en el slice y **no** en `OutboxCleaner` — meterlo ahí
      haría que `Shared/Messaging` nombrara una entidad de `Accounts`, que es la dirección
      de dependencias que prohíbe `rules.md` §4
- [x] `AllowCredentials()` en CORS: sin eso el navegador no manda la cookie a otro origen

## ⚠️ Dos trampas de cookies que habrían fallado EN SILENCIO

Las dos tienen la misma forma —el login responde 200, la cookie no se guarda y el refresh
falla siempre **sin un solo error en el servidor**— y por eso conviene que queden escritas:

1. **El prefijo `__Host-`**, que era el nombre propuesto. Obliga al navegador a exigir
   `Secure` **y** `Path=/` **y** ningún `Domain`, y si algo no cuadra descarta la cookie sin
   avisar. Aquí choca dos veces: el `Path` va acotado a `/api/v1/auth` y en local se sirve
   por HTTP. Se usa `rt` a secas; lo que aportaba el prefijo se cubre con `SameSite=Strict`.
2. **`Secure` decidido por el entorno.** La primera versión miraba `IsDevelopment()`, y el
   host de tests usa `"Testing"` y sirve por HTTP: marcaba la cookie como `Secure` y
   **ningún test de sesión podía pasar**. Lo cazaron ellos. Ahora se decide por el esquema
   real de la petición (`Request.IsHttps`), que se ajusta solo.
   ⚠️ Detrás de un proxy TLS eso depende de `UseForwardedHeaders`, que ya está puesto.

## La división que sostiene todo

Es la misma de `planning/17`, y por eso se decidió rápido:

| | Dónde | Qué pasa si falla |
|---|---|---|
| **Garantía**: «esta sesión no se puede extender» | Familia de refresh tokens, en la BASE, revocada en una transacción | Nada: si la base no está, tampoco hay login |
| **Optimización**: «y el access token que ya tienes muere ya» | Denylist de `jti` en Redis | El token sobrevive hasta expirar: **como mucho 15 minutos** |

Verificado con Redis apuntando a un puerto muerto: el logout **sigue cortando la sesión**
(refresh → 401) y lo único que sobrevive es el access token actual.

## Verificación

- [x] `dotnet build -warnaserror` sin warnings · **193 tests** en verde (eran 186).
- [x] **Ejecutando**, contra la API de verdad:

| Qué | Resultado |
|---|---|
| Login | access token en el cuerpo, refresh en cookie `httponly; samesite=strict; path=/api/v1/auth` |
| El refresh token **no** aparece en el cuerpo | correcto |
| Refresh | access token nuevo y cookie rotada |
| Reuso **dentro** de la gracia | 401, y la sesión legítima **sigue viva** |
| Reuso **fuera** de la gracia | 401 al ladrón **y a la víctima**: familia revocada |
| Logout | 204, familia revocada, y el access token que llevaba muere en el acto |
| Logout repetido / sin cookie | 204, inofensivo |
| Sin Redis | la sesión se corta igual; solo sobrevive el access token actual |

✏️ Una comprobación mía dio `HttpOnly=False` y **era un falso negativo**: Kestrel emite
`httponly` en minúscula y yo comparaba sensible a mayúsculas. Misma clase de error que el
`grep "[ERR]"` de `planning/19`. El test de la suite compara ya sin distinguir mayúsculas.

## Lo que queda abierto

- [ ] **No hay «cerrar sesión en todos los dispositivos»**: se revoca la familia de *esta*
      sesión, no todas las del usuario. Es lo que uno espera —salir en el móvil no cierra
      el portátil— pero falta el botón para cuando sí se quiere.
- [ ] **Cambiar la contraseña no revoca las sesiones abiertas.** Debería, y es una línea:
      revocar todas las familias del usuario. Entra con `planning/14`.
- [ ] **El cliente móvil no está cubierto.** La cookie es una decisión para SPA; un cliente
      nativo necesitaría el token en el cuerpo. Si aparece, se añade sin tocar el servicio:
      sólo cambia el adaptador.
