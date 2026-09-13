# language: en
Feature: Identificadores publicos no enumerables en las rutas
  El front usa URLs como /cuenta/pedidos/71: con la clave primaria en la ruta basta con ir
  sumando uno para descubrir recursos y medir el volumen del negocio. Las ordenes y los
  pagos pasan a un identificador publico aleatorio, y las categorias a un slug, igual que
  ya tenian los productos. La clave primaria queda interna.

  # --- Categorias -------------------------------------------------------------

  Scenario: Una categoria nace con un slug derivado de su nombre
    Given un administrador
    When crea la categoria "Ropa Hombre"
    Then la categoria tiene el slug "ropa-hombre"

  Scenario: Buscar una categoria por su slug es anonimo
    Given la categoria "Ropa Hombre"
    When un visitante pide GET /category/slug/ropa-hombre
    Then recibe 200 con la categoria

  Scenario: Un slug que no existe es 404
    When un visitante pide GET /category/slug/no-existe
    Then recibe 404

  Scenario: El slug de una categoria no cambia al renombrarla
    Given la categoria "Ropa Hombre"
    When un administrador la renombra a "Moda Hombre"
    Then su slug sigue siendo "ropa-hombre"
    # Cambiarlo romperia los enlaces que ya circulan.

  Scenario: Dos nombres que dan el mismo slug chocan con un 409
    Given la categoria "Ropa Hombre"
    When un administrador crea la categoria "Ropa  Hombre"
    Then recibe 409

  Scenario: Los productos de una categoria se piden por su slug
    Given la categoria "Ropa Hombre" con productos
    When un visitante pide GET /product/category/slug/ropa-hombre
    Then recibe 200 con esos productos
    And cada producto trae categorySlug

  # --- Ordenes ----------------------------------------------------------------

  Scenario: Una orden se identifica por su publicId, no por su clave primaria
    Given un comprador
    When coloca una orden
    Then la respuesta trae publicId y no trae id
    And la cabecera Location apunta a /order/{publicId}

  Scenario: Leer, descargar el comprobante y mover una orden va por publicId
    Given una orden colocada
    Then GET /order/{publicId}, GET /order/{publicId}/receipt y PATCH /order/{publicId}/status la encuentran

  Scenario: La ruta con la clave primaria ya no existe
    Given una orden colocada
    When su comprador pide GET /order/{id} con el entero
    Then recibe 404

  Scenario: El publicId de otro comprador es indistinguible de uno que no existe
    Given la orden de otro comprador
    When pido GET /order/{publicId}
    Then recibo 404

  # --- Pagos ------------------------------------------------------------------

  Scenario: Se paga una orden citando su publicId
    Given una orden colocada
    When su comprador manda POST /payment con orderPublicId
    Then recibe 201 con publicId y orderPublicId, sin id ni orderId

  Scenario: Sin orderPublicId el cuerpo es invalido
    When un comprador manda POST /payment sin orderPublicId
    Then recibe 400

  Scenario: Un pago se lee por su publicId
    Given un pago iniciado
    When su comprador pide GET /payment/{publicId}
    Then recibe 200
