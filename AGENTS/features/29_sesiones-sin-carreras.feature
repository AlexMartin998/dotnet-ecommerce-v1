# language: en
Feature: Cerrar sesion sin carreras, login sin oraculo de tiempo
  Salen de la revision del slice de auth de la API nueva (EcommerceApi): el login delataba por
  tiempo que usuarios existen, y se sospechaba que un refresh a la vez que un logout dejaba
  viva la sesion (no se confirmo: queda un test de guardia). Y lo que el front ya sufria.

  # --- Carreras de sesion (guardia: la sospecha NO se confirmo) ------------------------

  Scenario: Un logout durante una rotacion revoca el token que se esta emitiendo
    Given un refresh que ya consumio su token y aun no ha confirmado el siguiente
    When llega un logout con la misma cookie
    Then al terminar los dos no queda ningun token vivo de la familia
    # SQL Server bloquea el UPDATE del logout hasta el commit y revoca tambien la fila nueva,
    # con bloqueos y con RCSI. Pasa sin lock; falla si la revocacion lee y luego escribe.

  Scenario: Un logout-all durante una rotacion revoca el token que se esta emitiendo
    Given un refresh a mitad de rotar
    When llega un logout-all del mismo usuario
    Then no queda ningun token vivo de la familia

  # --- Oraculo de tiempo ---------------------------------------------------------------

  Scenario: Un usuario inexistente tarda lo mismo que una contrasena incorrecta
    When se intenta el login con un usuario que no existe
    Then la respuesta es 401 y tarda del orden de un usuario real con la contrasena mal
    # Sin hash ficticio, el inexistente respondia en ~1 ms y el real tras PBKDF2.

  # --- Lo que el front ya sufria --------------------------------------------------------

  Scenario: Refresh devuelve el usuario completo
    Given un usuario con nombre
    When refresca la sesion
    Then "user.name" y "user.createdAt" son los mismos que en el login

  Scenario: El limite estricto de auth no alcanza a refresh ni a me
    Given el limite de auth en 2 peticiones por ventana
    When se agotan con logins
    Then un tercer login es 429
    But refresh y me siguen respondiendo
    # Cada recarga del front llama a /refresh: con el limite de auth daba 429.

  Scenario: Reusar la cookie de una sesion ya cerrada no es una alarma de robo
    Given una sesion cerrada con logout
    When se presenta su cookie pasada la ventana de gracia
    Then es 401 y el log no avisa de reuso
