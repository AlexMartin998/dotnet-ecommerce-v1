# language: en
Feature: Categorias destacadas, listados paginados y orden estable de imagenes
  El header del front ensena hasta 3 categorias destacadas y el resto en un desplegable; la
  home y cada categoria usan infinite scroll; y la tarjeta ensena la segunda imagen al pasar
  el raton. Pedido por el front (api-contract-03-featured-categories-and-paging.md).

  # --- A. Destacadas -----------------------------------------------------------

  Scenario: Las destacadas se leen en orden y de forma anonima
    Given las categorias "Shirts", "Hoodies" y "Hats" destacadas en ese orden
    When un visitante pide GET /category/featured
    Then recibe 200 con las tres, ordenadas por "featuredPosition" 1, 2 y 3

  Scenario: Toda categoria dice si es destacada
    When un visitante pide GET /category
    Then cada categoria trae "featuredPosition", null si no es destacada

  Scenario: Un administrador fija las destacadas de una vez, en orden
    When un administrador hace PUT /category/featured con { categoryIds: [3, 1] }
    Then recibe 200 con la 3 en posicion 1 y la 1 en posicion 2
    And las que eran destacadas y no vienen dejan de serlo

  Scenario: Desmarcar todas
    When un administrador hace PUT /category/featured con { categoryIds: [] }
    Then GET /category/featured devuelve 200 con []

  Scenario: Una cuarta destacada es un error con code estable
    When un administrador hace PUT /category/featured con cuatro ids
    Then recibe 400 con code "featured_limit_reached"
    And las destacadas no cambian

  Scenario: La base impide mas de 3 aunque el servicio falle
    When se intenta guardar una categoria con featuredPosition 4 saltandose el servicio
    Then la base lo rechaza con su CHECK

  Scenario: Ids repetidos o inexistentes
    When un administrador manda un id repetido o uno que no existe
    Then recibe 400 y las destacadas no cambian

  Scenario: Solo un administrador fija destacadas
    When un usuario sin rol admin hace PUT /category/featured
    Then recibe 403

  # --- B. Paginacion ------------------------------------------------------------

  Scenario: Los productos de una categoria, paginados
    Given la categoria "hoodies" con 3 productos
    When un visitante pide GET /product/category/slug/hoodies/paged?page=1&pageSize=2 y luego page=2
    Then la primera pagina trae 2, la segunda 1, ninguno repetido, y totalItems 3

  Scenario: Una categoria que no existe
    When un visitante pide GET /product/category/slug/no-existe/paged
    Then recibe 404

  Scenario: La busqueda, paginada
    When un visitante pide GET /product/search/paged?name=hoodie&page=1&pageSize=2
    Then recibe un PagedResult con los que contienen "hoodie"

  Scenario: Una busqueda vacia no es un error
    When un visitante pide GET /product/search/paged sin name
    Then recibe 200 con una pagina vacia

  Scenario: El orden es total entre paginas
    Given productos creados en el mismo instante
    Then el orden es CreatedAt descendente con desempate por Id descendente

  # --- C. Imagenes --------------------------------------------------------------

  Scenario: Las imagenes salen en orden estable
    Given un producto con imagenes en las posiciones 0 y 1
    Then "images" trae primero la de posicion 0
    And dos imagenes con la misma posicion se desempatan por Id
