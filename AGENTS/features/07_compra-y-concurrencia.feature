Feature: Compra y condiciones de carrera
  Como dueño de la tienda
  quiero que nunca se venda stock que no existe
  ni se rechacen compras válidas por contención

  # ⚠️ Estos escenarios SOLO tienen valor con peticiones SIMULTÁNEAS. Secuencialmente
  # pasaban también con la implementación defectuosa.

  Scenario: Compra correcta descuenta stock
    Given el producto "SKU-1" tiene stock 10
    And tengo un token de un usuario autenticado
    When compro 2 unidades de "SKU-1"
    Then recibo 200
    And el stock queda en 8

  Scenario: Stock insuficiente es 409
    Given el producto "SKU-1" tiene stock 1
    When compro 5 unidades
    Then recibo 409 con code "conflict"
    And el stock sigue siendo 1

  Scenario: SKU inexistente es 404
    When compro un SKU que no existe
    Then recibo 404 con code "not_found"

  # MEDIDO: con RowVersion + reintentos daba 5x200 + 5x409 y stock 5 —no sobrevendía,
  # pero rechazaba compras válidas. Con UPDATE condicional atómico: 10x200 + 5x409.
  Scenario: Quince compras simultáneas sobre stock diez
    Given el producto "SKU-R" tiene stock 10
    When 15 clientes compran 1 unidad a la vez
    Then exactamente 10 reciben 200
    And exactamente 5 reciben 409
    And el stock final es 0

  Scenario: Nunca se vende stock negativo
    Given el producto "SKU-R" tiene stock 3
    When 20 clientes compran 1 unidad a la vez
    Then el stock final es 0 y nunca negativo

  # Sin [Range(1, ...)] un Quantity negativo pasaría `Stock >= quantity` y
  # `Stock - quantity` AUMENTARÍA el stock.
  Scenario Outline: Cantidades inválidas se rechazan antes de tocar la base
    When compro <cantidad> unidades
    Then recibo 400 con errores de validación

    Examples:
      | cantidad |
      | 0        |
      | -5       |

  # La unicidad aplicativa se comprueba antes de escribir, pero entre esa comprobación
  # y el INSERT cabe otra petición. El índice único de la BASE es la garantía real.
  Scenario: Ocho creaciones simultáneas de la misma categoría
    Given tengo un token de administrador
    When 8 peticiones crean a la vez la categoría "Duplicada"
    Then exactamente 1 recibe 201
    And 7 reciben 409 con code "conflict"
    And existe exactamente 1 fila con ese nombre

  # Sin traducir la excepción, arreglar la carrera EMPEORA la respuesta: el 409 correcto
  # se convierte en un 500.
  Scenario Outline: Los choques de la base se traducen a 409, no a 500
    When se produce <error> de SQL Server
    Then el cliente recibe 409 con code <code>

    Examples:
      | error                          | code                  |
      | violación de índice único      | "conflict"            |
      | deadlock (1205)                | "deadlock"            |
      | violación de clave foránea     | "fk_violation"        |
      | conflicto de concurrencia (EF) | "concurrency_conflict"|

  Scenario: La marca de tiempo usa el reloj de la aplicación
    When compro un producto
    Then "updatedAt" es coherente con el reloj del proceso, no con el del servidor SQL
