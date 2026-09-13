# language: en
Feature: Catalogo de demostracion traido de Teslo Shop
  El catalogo sembrado eran siete productos inventados y sin imagenes ("Portatil 14\"",
  "Cafetera italiana"), que no dejaban ver el front como una tienda. Se sustituye por el
  seed de la tienda Next.js (Teslo Shop): 52 prendas con descripcion, tallas, etiquetas y
  dos imagenes cada una. Solo cambia lo que se siembra; el contrato HTTP no se toca.

  Background:
    Given el seeding encendido con Seed:IncludeDemoData = true
    And una base sin categorias

  Scenario: Se siembran las categorias por tipo de prenda
    When la API arranca
    Then existen las categorias "Shirts", "Pants", "Hoodies" y "Hats"
    And "Pants" no tiene productos y su listado es 200 con []

  Scenario: Se siembran los 52 productos con su categoria
    When la API arranca
    Then existen 52 productos
    And "Men's Chill Crew Neck Sweatshirt" pertenece a "Shirts"

  Scenario: El genero del front viaja como etiqueta
    When la API arranca
    Then el producto con slug "mens-chill-crew-neck-sweatshirt" tiene las etiquetas "sweatshirt" y "men"
    # Product no tiene genero: el front lo filtra por etiqueta, que es para lo que existen.

  Scenario: El slug del origen se conserva, con guiones
    When la API arranca
    Then GET /product/slug/mens-chill-crew-neck-sweatshirt devuelve 200
    # Derivarlo del nombre daria "men-s-chill-..." por el apostrofo tipografico.

  Scenario: El SKU es la referencia real de la prenda
    When la API arranca
    Then el producto "Men's Chill Crew Neck Sweatshirt" tiene el SKU "1740176-00-A"

  Scenario: Cada producto nace con sus dos imagenes, en orden
    When la API arranca
    Then cada producto tiene 2 imagenes con posiciones 0 y 1
    And GET de la url de cada imagen devuelve 200 con la imagen

  Scenario: Las imagenes pasan por el almacenamiento, no se copian a mano
    When la API arranca
    Then cada imagen se guardo con IFileStorage y su url es relativa bajo /ProductsImages/
    # Es el mismo camino que una subida del admin: validacion incluida, y manana S3.

  Scenario: Sembrar es idempotente
    Given el catalogo ya sembrado
    When la API arranca otra vez
    Then no se crea ningun producto ni ninguna imagen nueva

  Scenario: Sin las imagenes de origen el catalogo se siembra igual
    Given la carpeta de imagenes de demo no existe en el despliegue
    When la API arranca
    Then se siembran los 52 productos sin imagenes
    And queda un warning en el log

  Scenario: Los tests no siembran la demo
    When arranca el host de los tests de integracion
    Then no se siembra el catalogo de demostracion
    # Con imagenes, cada corrida dejaria 104 ficheros nuevos en wwwroot.
