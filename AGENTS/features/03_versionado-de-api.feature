Feature: Versionado de la API por segmento de URL
  Como consumidor de la API
  quiero que la versión viaje en la URL
  para poder verla en un log, cachearla y compartirla como enlace

  Scenario: Las rutas viven bajo la versión
    When pido "GET /api/v1/category"
    Then recibo 200

  # AssumeDefaultVersionWhenUnspecified es humo con versionado por ruta: /api/category
  # no matchea ninguna plantilla y da 404 antes de que el versionador opine.
  Scenario: Una ruta sin versión no existe
    When pido "GET /api/category"
    Then recibo 404

  Scenario: La respuesta anuncia las versiones soportadas
    When pido "GET /api/v1/category"
    Then la respuesta trae la cabecera "api-supported-versions"

  # Sin el parámetro `version`, la generación del header Location falla con un 500
  # que no parece tener relación con el POST.
  Scenario: El Location de un 201 conserva la versión pedida
    Given tengo un token de administrador
    When creo una categoría llamando a "POST /api/v1/category"
    Then recibo 201
    And la cabecera "Location" apunta a "/api/v1/Category/{id}"

  Scenario: La versión se devuelve en el mismo dialecto en que se pidió
    Given tengo un token de administrador
    When creo una categoría llamando a "POST /api/v1.0/category"
    Then la cabecera "Location" contiene "/api/v1.0/"

  # Los controllers de infraestructura no forman parte del contrato versionado.
  Scenario: Las sondas de salud son neutrales a la versión
    When pido "GET /health"
    Then recibo 200

  Scenario: Swagger publica un documento por versión descubierta
    Given el entorno es "Development"
    When pido "GET /swagger/v1/swagger.json"
    Then recibo 200
    And el documento incluye la definición de seguridad "Bearer"
