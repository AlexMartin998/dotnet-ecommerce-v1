# 29 — Cerrar sesión sin carreras, login sin oráculo de tiempo

> Del owner (2026-09-13): «sí por fa y autónomo», tras la revisión del slice de auth de
> `EcommerceApi` que hizo esta sesión para `api_clean`: dos de sus hallazgos importantes
> aplicaban también aquí. Se añaden los problemas de esta API ya señalados en el informe de
> auth. Contrato: `features/29_sesiones-sin-carreras.feature`. Nada de §11.1.

---

## 0. Qué y por qué

| # | Problema | Arreglo |
|---|---|---|
| 1 | ✏️ **Sospecha: refresh activo en carrera con logout / logout-all / password** dejaría viva la sesión (el UPDATE del cierre no vería la fila que el refresh inserta). **NO se confirmó** — ver §2 | Ningún cambio de código. Queda un test de guardia con la ventana forzada |
| 2 | **Oráculo de tiempo en login**: usuario inexistente → 401 en ~1 ms; real con contraseña mala → 401 tras PBKDF2 | Verificar un hash ficticio también cuando no existe |
| 3 | Refresh devolvía el `user` **sin `name` ni `createdAt`**: el front restaura la sesión con refresh | La misma proyección que el login (`IdentityMapping.ToDto`) |
| 4 | La política `auth` (10/min por IP) se aplicaba a **todo** el controller: cada recarga del front llama a `/refresh` → 429 | `auth` solo en register, login y password. `refresh` con política propia (30/min por IP). me, logout y logout-all bajo el global. ⚠️ Se diseñó por **cookie** y se descartó al escribir el test: la cookie rota en cada refresh, cada petición caería en una partición nueva y el límite no limitaría nada |
| 5 | Presentar la cookie de una sesión ya cerrada pasada la gracia logueaba «REUSE detected» | Warning solo si la revocación encontró tokens vivos |
| 6 | `docs/08`: `POST /auth/password` con la actual mal decía 400 | Es 401 |

Lo que **no** se toca: el 403 de lockout (también delata que el usuario existe, pero es contrato
con el front) y las claves de `errors` (`Password` en register, `newPassword` en password).

## 1. Checklist

- [x] ~~`sp_getapplock` por usuario en rotación y revocaciones~~ — implementado y **retirado** (§2).
- [x] `AuthService.LoginAsync`: hash ficticio con el hasher real, calculado una vez.
- [x] Refresh con `IdentityMapping.ToDto`.
- [x] Rate limit: `auth` en register/login/password; `refresh` propio por IP (30/min).
- [x] Log de reuso: Warning solo si había tokens vivos.
- [x] `docs/08`: password con la actual mal → 401.

## 2. 🔴 La carrera que no existía, y cómo se supo

1. Se añadió el `sp_getapplock` y tests con refresh y logout a la vez por HTTP (15, luego 40
   vueltas escalonadas 0–20 ms). **Pasaban igual QUITANDO el lock**: no probaban nada.
2. Se repitió con la base en **READ_COMMITTED_SNAPSHOT**, que es donde en teoría el cierre leería
   la foto sin la fila nueva. **Seguían pasando sin lock.**
3. Se forzó la ventana: un decorador del repositorio espera **400 ms** entre consumir el token y
   confirmar el siguiente, sobre una base propia en RCSI (`HeldRotationFactory`). Diagnóstico sin
   lock: `logout=204@488ms refresh=200@488ms rcsi=True live=0` — el logout **esperó** al commit
   del refresh y revocó también la fila nueva.
4. Por qué: el UPDATE de SQL Server toma bloqueos de actualización sobre las filas que afecta y
   reevalúa con la última versión confirmada, también en RCSI; la fila nueva de la familia queda
   en su recorrido. **El lock no arreglaba nada → retirado.**
5. Para que el test no sea otro de los que no prueban nada, se rompió la revocación a
   **leer ids y después actualizar**: falla (`Expected 0, Actual 1`). Queda como guardia de ese
   patrón (`SessionLockTests`).

Se le corrigió a la sesión `api_clean`, a la que se le había dado como hallazgo importante.

## 2.bis Un flaky de la infraestructura de tests, destapado de paso

La primera suite completa dio **52 fallos** (con la máquina sin memoria y 2 m 38 s) y la
siguiente, 1: un 404 en `FeaturedAndPagingTests`. Causa: `ApiFactory` **borra la base** al
empezar, pero **no la cache de Redis** (`apiecommerce-tests:`, TTL 60 s). Dos corridas seguidas:
la base renace con los ids desde 1, `GET /category/{id}` sirve la categoría de la corrida
anterior con ese id y otro slug, y el listado por slug da 404. Las fábricas con base compartida
(la nueva de rate limit) lo empeoraban borrándola a mitad de suite.

- [x] `ApiFactory.InitializeAsync` borra las claves `apiecommerce-tests:*` (solo ese prefijo: el
      Redis es compartido).
- [x] `DatabaseName` virtual: `TightAuthLimitFactory` y `HeldRotationFactory` usan base propia.
- [x] Dos suites completas seguidas: **437/437** y **437/437**.

## 3. Verificación

- [x] Build `-warnaserror` limpio y suite **437/437** (+4: guardia de rotación ×2, tiempo de
      login, usuario completo en refresh, rate limit; y el unit test del login verifica el hash).
- [x] **Cada test nuevo falla sin su arreglo**: hash ficticio (mediana 2,3 ms frente a 30 ms),
      política de auth en la clase (`/me` → 429), revocación leer-y-escribir (1 token vivo).
