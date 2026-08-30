Feature: Cache de catálogo en Redis
  Como operador
  quiero que las lecturas públicas se sirvan desde Redis
  para no golpear la base en cada petición, y que se invaliden al escribir

  # Se prefiere esto a [ResponseCache] porque aquel vive en la memoria de UN proceso,
  # no cachea nada si el request lleva cabecera Authorization, y NO se puede invalidar.

  Scenario: La segunda lectura se sirve de cache
    Given Redis está disponible
    When pido dos veces "GET /api/v1/category"
    Then la primera registra "Cache MISS" y la segunda "Cache HIT"

  Scenario: Una escritura invalida la cache
    Given he leído "GET /api/v1/category/{id}" y está cacheada
    When actualizo esa categoría con PATCH
    And vuelvo a pedirla
    Then recibo el valor NUEVO, no el cacheado

  # Si el servicio interno lanza (409 por nombre duplicado), no se debe tirar una cache
  # que sigue siendo válida.
  Scenario: Una escritura fallida no invalida la cache
    Given la cache de categorías está poblada
    When intento crear una categoría con un nombre que ya existe
    Then recibo 409
    And la entrada de cache sigue existiendo

  # Una cache que tumba la API convierte una optimización en punto único de fallo.
  Scenario: Con Redis caído la API sigue respondiendo
    Given Redis no está disponible
    When pido "GET /api/v1/category"
    Then recibo 200 con los datos leídos de la base

  Scenario: Sin Redis configurado la aplicación arranca igual
    Given "Redis:Configuration" está vacío
    When arranco la aplicación
    Then arranca correctamente y las lecturas no se cachean

  # Cachear listados paginados exige invalidar por prefijo o versionar la clave de
  # colección: cada page/pageSize sería una clave que ninguna invalidación conoce.
  Scenario: Los listados paginados NO se cachean
    When pido "GET /api/v1/category/paged?page=1&pageSize=2" dos veces
    Then ninguna de las dos registra un "Cache HIT"

  # Una cache compartida con datos por usuario es una fuga de datos entre cuentas.
  Scenario: Solo se cachea lo público
    Then ningún endpoint que dependa del usuario autenticado usa la cache
