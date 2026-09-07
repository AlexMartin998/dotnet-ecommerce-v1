# Keywords en inglés, descripciones en español (AGENTS/rules.md §10).
# Implementado en 34a3b44. Estos escenarios son la especificación de los tests del paso 7.

Feature: Registro y autenticación
  Como cliente de la API
  quiero registrarme y obtener un token
  para poder consumir los endpoints protegidos

  Background:
    Given la API está arrancada
    And existe el usuario "admin" con rol "admin"

  Scenario: Registro correcto devuelve 201 y token
    When registro el usuario "nuevo" con email "nuevo@x.com" y contraseña "Test1234"
    Then recibo 201
    And la respuesta trae un token JWT y el perfil del usuario
    And el usuario queda con rol "user"

  # El rol NO se acepta en el body a propósito: en el curso de referencia el registro era
  # anónimo y aceptaba "Role": "Admin", así que cualquiera se auto-promovía a administrador.
  Scenario: El registro nunca concede el rol admin
    When registro un usuario enviando además el campo "role" con valor "admin"
    Then el usuario creado tiene rol "user" y no "admin"

  Scenario Outline: Registro con datos ya usados devuelve 409
    Given existe un usuario con username "tomado" y email "tomado@x.com"
    When registro un usuario con <campo> repetido
    Then recibo 409 con code "conflict"

    Examples:
      | campo    |
      | username |
      | email    |

  # 422 y no 400: la forma del DTO era válida; lo que falla es la política de Identity.
  Scenario: Contraseña que no cumple la política devuelve 422
    When registro un usuario con contraseña "alllowercase"
    Then recibo 422 con code "validation_error"
    And el detalle de errores incluye el campo "Password"

  Scenario: Login correcto devuelve token con expiración
    When hago login como "admin" con contraseña "Admin123!"
    Then recibo 200
    And la respuesta trae token y "expiresAt"

  # Mismo mensaje en los dos casos: distinguirlos convierte el login en un oráculo
  # para enumerar usuarios existentes.
  Scenario Outline: Credenciales inválidas no revelan si el usuario existe
    When hago login con <usuario> y contraseña incorrecta
    Then recibo 401 con detalle "Invalid username or password."

    Examples:
      | usuario              |
      | un usuario existente |
      | un usuario que no existe |

  Scenario: Cinco intentos fallidos bloquean la cuenta
    Given el usuario "victima" existe
    When fallo el login 5 veces seguidas
    And hago login con la contraseña correcta
    Then recibo 403 con detalle sobre el bloqueo temporal

  Scenario: El perfil requiere token
    When pido "GET /api/v1/auth/me" sin token
    Then recibo 401
    And el cuerpo es un ProblemDetails, no una respuesta vacía

  Scenario: El perfil devuelve el usuario del token
    Given tengo un token válido de "admin"
    When pido "GET /api/v1/auth/me" con ese token
    Then recibo 200 con username "admin" y roles ["admin"]
    And la respuesta NO contiene contraseña ni hash

  # --- Lo que no se ve probando de uno en uno --------------------------------

  Scenario: Dos registros simultaneos con el mismo email dejan una sola cuenta
    Given seis registros del mismo email a la vez
    Then exactamente uno recibe 201
    And el resto recibe 409
    And ninguno recibe un 5xx
    # RequireUniqueEmail de Identity comprueba antes de insertar, asi que es el mismo
    # leer-y-escribir: sin indice unico en la base se crean dos cuentas. Y a partir de
    # ahi ese email queda ENVENENADO, porque FindByEmailAsync hace SingleOrDefault y
    # lanza: el registro siguiente da 500 para siempre, no 409.

  Scenario: El choque de email siempre contesta lo mismo
    Given un email que ya existe
    When intento registrarme con el
    Then recibo 409, tanto si lo detecta la validacion como si lo detecta el indice
    # Antes contestaba 422 o 409 segun quien se adelantara en la carrera.
