Feature: Administrar usuarios sin tocar la base a mano
  Como administrador
  quiero listar usuarios, darles y quitarles roles y bloquear cuentas
  porque hoy el UNICO camino para tener un admin es el seeder

  Background:
    Given estoy autenticado como administrador

  # --- Consulta ---------------------------------------------------------------

  Scenario: Listado paginado
    When pido "GET /api/v1/user?page=1&pageSize=10"
    Then recibo una pagina con el total y los enlaces de navegacion
    And ningun usuario trae hash ni contraseña

  Scenario: Solo los administradores pueden mirar
    Given estoy autenticado como un usuario normal
    When pido el listado
    Then recibo 403

  # --- Roles ------------------------------------------------------------------

  Scenario: Promover a administrador
    When asigno el rol "admin" a otro usuario
    Then ese usuario pasa a tener el rol
    And queda constancia en el log de quien promovio a quien

  Scenario: Un rol que no existe se rechaza
    When asigno el rol "superuser"
    Then recibo 400
    # Identity crearia el rol al vuelo, y acabariamos con roles fantasma que no
    # protegen nada porque ningun [Authorize] los nombra.

  Scenario: Asignar un rol que ya se tiene es inofensivo
    Given el usuario ya es "admin"
    When le asigno "admin" otra vez
    Then recibo 204 y nada cambia

  # --- Las reglas duras: las que un descuido rompe ----------------------------

  Scenario: Un admin no puede quitarse a si mismo el rol admin
    When intento quitarme el rol "admin"
    Then recibo 409
    And sigo siendo administrador
    # Un clic y el sistema se queda sin nadie que pueda administrarlo.

  Scenario: No se puede quitar el rol al ultimo administrador
    Given solo queda un administrador y no soy yo
    When le quito el rol "admin"
    Then recibo 409
    And el sistema sigue teniendo un administrador

  Scenario: Un admin no puede bloquearse a si mismo
    When intento bloquear mi propia cuenta
    Then recibo 409

  # --- Bloqueo ----------------------------------------------------------------

  Scenario: Bloquear una cuenta la deja fuera
    When bloqueo a un usuario
    Then no puede volver a hacer login

  # ⚠️ Sin esto, bloquear una cuenta no sirve de nada: el usuario sigue dentro con su
  # access token y, peor, puede seguir renovandolo indefinidamente.
  Scenario: Bloquear corta las sesiones que ya estaban abiertas
    Given un usuario con la sesion abierta
    When lo bloqueo
    Then sus refresh tokens quedan revocados
    And no puede renovar su sesion

  Scenario: Un usuario bloqueado no puede renovar aunque le quede refresh token
    Given un usuario bloqueado que conserva su cookie
    When intenta renovar
    Then recibe 401

  Scenario: Desbloquear le devuelve el acceso
    Given un usuario bloqueado
    When lo desbloqueo
    Then puede volver a hacer login

  # --- Lo que no se ve probando de uno en uno --------------------------------

  Scenario: Varios administradores degradandose a la vez no dejan el sistema sin ninguno
    Given cuatro administradores
    When cada uno degrada al siguiente al mismo tiempo
    Then queda al menos un administrador
    And los que sobran reciben 409
    # Comprobar el recuento y borrar el rol son dos viajes a la base: entre uno y otro
    # cabe la comprobacion del otro, y los dos creen que sobra un admin. Medido con
    # cuatro en corro: 4x204 y CERO administradores, sin vuelta atras por la API porque
    # ya nadie puede reasignar el rol.
