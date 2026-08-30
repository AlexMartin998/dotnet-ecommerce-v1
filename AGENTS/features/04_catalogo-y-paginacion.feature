Feature: Catálogo y paginación
  Como consumidor de la API
  quiero listados paginados con metadatos
  para poder pintar un paginador sin traerme la tabla entera

  Scenario: El listado paginado devuelve items y metadatos
    Given existen 3 categorías
    When pido "GET /api/v1/category/paged?page=1&pageSize=2"
    Then recibo 200
    And la respuesta trae "items", "page", "pageSize", "totalItems", "totalPages", "hasNext", "hasPrevious"
    And "totalItems" vale 3 y "totalPages" vale 2

  # 404 es "el recurso no existe"; la colección SÍ existe, lo que no hay son resultados.
  # El código de referencia devolvía 404 con la tabla vacía.
  Scenario: Una página fuera de rango devuelve 200 con lista vacía
    When pido "GET /api/v1/product/paged?page=999&pageSize=5"
    Then recibo 200
    And "items" está vacío
    And "totalItems" refleja el total real

  # Sin tope, ?pageSize=1000000 en un endpoint anónimo es una denegación de servicio
  # de una sola petición.
  Scenario Outline: Los parámetros de paginación están acotados
    When pido "GET /api/v1/product/paged?<query>"
    Then recibo 400 con errores de validación

    Examples:
      | query          |
      | pageSize=9999  |
      | pageSize=0     |
      | page=0         |

  # Un Skip/Take sin orden estable puede repetir una fila en dos páginas y saltarse otra.
  Scenario: El orden es estable entre páginas
    Given existen 20 productos creados en el mismo instante
    When recorro todas las páginas de tamaño 5
    Then cada producto aparece exactamente una vez

  # El repositorio genérico no carga la navegación, y el campo saldría vacío en silencio.
  Scenario: El listado paginado de productos trae el nombre de la categoría
    Given existe un producto en la categoría "Electronica"
    When pido "GET /api/v1/product/paged?page=1&pageSize=10"
    Then cada item trae "categoryName" con valor

  Scenario: Un listado sin resultados es 200 con lista vacía
    When busco productos con "GET /api/v1/product/search?name=noexiste"
    Then recibo 200
    And el cuerpo es una lista vacía
