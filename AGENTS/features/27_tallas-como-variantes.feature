# language: en
Feature: Tallas seleccionables con stock por talla
  Hasta ahora las tallas eran una lista informativa y el stock era por producto: el cliente
  no podia elegir talla y la talla no llegaba a la orden. Cada talla pasa a ser una VARIANTE
  del producto, con SKU propio y stock propio. La clave de una linea de carrito sigue siendo
  el `sku`, que ahora es el de la variante, asi que la forma de la cotizacion y de la orden
  no cambia. Pedido por el front (api-contract-02-product-sizes.md).

  # --- Catalogo publico (R1, R5) ------------------------------------------------

  Scenario: La ficha devuelve las tallas activas con su disponibilidad, en orden
    Given el producto "Men's Chill Crew Neck Sweatshirt" con las tallas XS..XXL y XXL agotada
    When un visitante pide GET /product/slug/mens-chill-crew-neck-sweatshirt
    Then "variants" trae una entrada por talla activa, ordenada por "position"
    And cada una lleva "sku", "size", "stock" y "available"
    And la de XXL tiene stock 0 y available false

  Scenario: Una talla desactivada no sale en la ficha publica
    Given la talla "XS" del producto desactivada
    When un visitante pide la ficha
    Then "variants" no incluye XS y "sizes" tampoco

  Scenario: El stock del producto es la suma de sus tallas activas
    When un visitante pide la ficha
    Then "stock" es la suma del stock de "variants"

  Scenario: Un producto sin tallas tiene una unica variante sin talla
    Given el producto "Relaxed T Logo Hat", que se vende sin tallas
    When un visitante pide la ficha
    Then "variants" tiene una sola entrada con "size" null y el mismo "sku" que el producto

  # --- Cotizacion y orden (R2, R3, R4, R7) ----------------------------------------

  Scenario: Cotizar una talla con stock
    When un visitante cotiza [{ sku: "1740176-00-A-M", quantity: 1 }]
    Then la linea viene con status "ok" y size "M"

  Scenario: Cotizar una talla agotada es un 200 con la linea marcada
    When un visitante cotiza [{ sku: "1740176-00-A-XXL", quantity: 1 }]
    Then recibe 200 y la linea viene con status "insufficient_stock" y available 0

  Scenario: Cotizar una talla desactivada
    Given la talla "XS" desactivada
    When un visitante cotiza su sku
    Then la linea viene con status "unavailable"

  Scenario: Comprar una talla guarda la talla en la linea de la orden
    When un comprador coloca una orden con [{ sku: "1740176-00-A-M", quantity: 2 }]
    Then recibe 201 y la linea trae size "M"
    And el stock de la talla M baja en 2 y el de las demas tallas no cambia

  Scenario Outline: Los fallos de linea al comprar llevan un code estable y el sku
    When un comprador coloca una orden con una linea <caso>
    Then recibe 409 con code "<code>" y la extension "sku"
    And no se aparta stock de ninguna linea

    Examples:
      | caso                           | code               |
      | con un sku que no existe       | sku_not_found      |
      | con una talla desactivada      | sku_unavailable    |
      | con mas unidades que stock     | insufficient_stock |

  Scenario: Comprar un producto sin tallas sigue funcionando con su sku
    When un comprador coloca una orden con el sku del producto sin tallas
    Then recibe 201 y la linea trae size null

  Scenario: Cancelar por abandono devuelve el stock a la talla comprada
    Given una orden sin pagar de 2 unidades de la talla M
    When pasa el recolector de ordenes abandonadas
    Then el stock de la talla M vuelve a subir en 2

  # --- Pedidos antiguos (R8) ----------------------------------------------------

  Scenario: Una linea sin variante se sigue leyendo
    Given una linea de orden anterior a las variantes, sin VariantId ni Size
    When su comprador pide la orden
    Then recibe 200 con size null

  # --- Administracion (R6) --------------------------------------------------------

  Scenario: Crear un producto con tallas
    When un administrador crea un producto con variants [{ size: "S", stock: 3 }, { size: "M", stock: 0 }]
    Then recibe 201 y el producto tiene dos variantes con sku "<SKU>-S" y "<SKU>-M"

  Scenario: Crear un producto sin tallas
    When un administrador crea un producto con stock 5 y sin variants
    Then tiene una variante sin talla, con el sku del producto y stock 5

  Scenario: No se mandan a la vez stock de producto y tallas
    When un administrador crea un producto con stock 5 y variants
    Then recibe 400

  Scenario: Las variantes de admin incluyen las desactivadas
    When un administrador pide GET /product/{id}/variants
    Then recibe todas, activas e inactivas

  Scenario: Anadir una talla
    When un administrador hace POST /product/{id}/variants con { size: "XL", stock: 4 }
    Then recibe 201 con la variante y sku "<SKU>-XL"

  Scenario: Una talla repetida es 409
    When un administrador anade una talla que el producto ya tiene
    Then recibe 409 con code "duplicate_size"

  Scenario: No se mezclan tallas con la variante sin talla activa
    Given un producto sin tallas
    When un administrador le anade la talla "M"
    Then recibe 409 con code "variant_kind_mismatch"
    # Primero se desactiva la variante sin talla; despues ya se pueden anadir tallas.

  Scenario: Desactivar una talla no rompe los pedidos antiguos
    Given una orden con una linea de la talla "S"
    When un administrador hace PATCH /product/{id}/variants/{variantId} con { isActive: false }
    Then recibe 200 y la orden se sigue leyendo con size "S"

  Scenario: Reponer stock de una talla
    When un administrador hace PATCH de la variante con { stock: 20 }
    Then su stock es 20

  Scenario: Editar una talla que una compra acaba de tocar es 412
    Given un administrador ha leido la variante con su "rowVersion"
    And una compra descuenta de esa talla
    When el administrador guarda su PATCH con If-Match de la version leida
    Then recibe 412
    # Sin If-Match el PATCH se aplica como siempre, igual que el del producto.
