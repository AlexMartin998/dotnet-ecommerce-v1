Feature: Sesiones que se pueden revocar
  Como dueño de la API
  quiero poder cortar una sesion y que los tokens robados dejen de valer
  porque hoy un access token robado vale 60 minutos y no hay forma de invalidarlo

  # Diseño fijado con el owner el 2026-09-06: el refresh token viaja en COOKIE HttpOnly.
  # Es lo mas seguro frente a XSS -el JS del navegador no puede leerlo- a cambio de
  # exigir CORS con credenciales y de complicar a un cliente movil, que no tiene
  # cookies gratis. Se asume: hoy el consumidor previsto es una SPA.

  Background:
    Given un usuario registrado

  # --- Emision ---------------------------------------------------------------

  Scenario: El login entrega dos cosas distintas por dos canales distintos
    When hago login
    Then recibo el access token en el cuerpo
    And recibo el refresh token en una cookie "HttpOnly", "Secure" y "SameSite=Strict"
    And el refresh token NO aparece en el cuerpo de la respuesta
    # Si viajara en el cuerpo, cualquier XSS podria leerlo y la cookie no serviria de nada.

  Scenario: En la base no se guarda el token, sino su huella
    When se emite un refresh token
    Then lo que se almacena es un SHA-256 del token
    # Misma logica que una contraseña: si la base se filtra, los tokens no son usables.

  # --- Rotacion --------------------------------------------------------------

  Scenario: Cada uso gasta el token y entrega uno nuevo
    Given tengo un refresh token valido
    When lo uso en "POST /api/v1/auth/refresh"
    Then recibo un access token nuevo
    And recibo un refresh token nuevo en la cookie
    And el anterior queda revocado

  Scenario: Un refresh token expirado no sirve
    Given tengo un refresh token caducado
    When lo uso
    Then recibo 401
    And no se emite nada

  # --- Deteccion de robo -----------------------------------------------------

  # La rotacion sola no detecta nada: lo que delata al ladron es REUSAR un token ya
  # gastado, porque el legitimo ya lo cambio por otro.
  Scenario: Reusar un token ya gastado revoca la familia entera
    Given use mi refresh token y recibi uno nuevo
    And ha pasado la ventana de gracia
    When alguien vuelve a usar el token viejo
    Then recibe 401
    And TODOS los refresh tokens de esa familia quedan revocados
    And mi sesion tambien se corta
    # Duro a proposito: si hay dos copias del token circulando, no se sabe cual es la mia.

  # ⚠️ Sin esta ventana, dos peticiones en paralelo de un cliente legitimo -que es lo
  # normal en un movil o una SPA- se ven como un robo y cierran la sesion sin parar.
  Scenario: Dos refrescos simultaneos del cliente legitimo NO cierran la sesion
    Given uso mi refresh token dos veces casi a la vez
    Then una recibe tokens nuevos
    And la otra recibe 401
    But la familia NO se revoca
    And la sesion sigue viva con el token nuevo

  # --- Cierre de sesion ------------------------------------------------------

  Scenario: El logout corta la sesion
    Given tengo una sesion abierta
    When hago "POST /api/v1/auth/logout"
    Then mi familia de refresh tokens queda revocada
    And la cookie se borra
    And no puedo volver a refrescar

  Scenario: El logout tambien invalida el access token que llevo en la mano
    Given tengo una sesion abierta
    When hago logout
    Then el access token que ya tenia deja de valer
    # Su `jti` entra en una denylist con TTL igual a lo que le quedaba de vida.

  # ⚠️ La denylist es una OPTIMIZACION, no la garantia. La garantia -"esta sesion no se
  # puede extender"- vive en la base, en la misma transaccion. Lo que se pierde sin
  # Redis es que el access token actual muera en el acto, y dura como mucho 15 minutos.
  Scenario: Sin Redis, el logout sigue cortando la sesion
    Given Redis no esta disponible
    When hago logout
    Then la familia queda revocada igualmente
    And no puedo refrescar
    But el access token que ya tenia sigue valiendo hasta que expire

  # --- Mantenimiento ---------------------------------------------------------

  Scenario: Los refresh tokens caducados se purgan
    Given hay refresh tokens caducados hace tiempo
    When pasa el recolector
    Then sus filas se borran
