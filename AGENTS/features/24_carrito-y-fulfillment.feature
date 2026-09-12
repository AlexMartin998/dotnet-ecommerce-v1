# language: en
Feature: Cotizar el carrito y mover la orden por su ciclo de vida
  Dos huecos que un front de tienda da por sentados y aqui no existian: ver el carrito con
  precios y stock ACTUALES antes de comprar, y que un administrador pueda mover una orden
  pagada hasta entregada. Y los contadores que necesita un panel de administracion.

  Background:
    Given un catalogo con productos y stock

  # --- Cotizar el carrito ------------------------------------------------------

  Scenario: Cotizar es anonimo porque el carrito existe antes de la sesion
    Given un visitante sin autenticar
    When cotiza un carrito con SKU y cantidades
    Then recibe 200 con el precio y el stock actuales de cada linea
    # Obligar a iniciar sesion para ver el carrito es pedir la cuenta antes del producto.

  Scenario: El cliente NUNCA manda precios
    When cotizo un carrito
    Then el cuerpo solo lleva SKU y cantidad
    And los precios, el desglose y el total los pone el servidor
    # Si el cliente manda precios hay que validarlos, y un cambio legitimo de precio
    # rompe el carrito.

  Scenario: Cotizar NO aparta stock
    Given un producto con stock 5
    When cotizo 5 unidades de ese producto
    Then el stock del catalogo sigue siendo 5
    # Una cotizacion es una foto, no una promesa: si apartara, un carrito abandonado
    # dejaria el catalogo vacio.

  Scenario: Una linea sin stock suficiente NO rompe la cotizacion
    Given un producto con stock 2
    When cotizo 10 unidades de ese producto
    Then recibo 200
    And esa linea sale marcada como no disponible con su maximo real
    And el total solo suma las lineas disponibles
    # El front necesita ENSENAR "solo quedan 2". Un 409 dejaria el carrito inservible.

  Scenario: Un SKU que no existe se marca, no revienta
    When cotizo un SKU inexistente
    Then recibo 200 con esa linea marcada como no encontrada

  Scenario: Los SKU repetidos se agrupan igual que al comprar
    When cotizo dos veces el mismo SKU con cantidades 2 y 3
    Then sale una sola linea con cantidad 5

  Scenario: La cotizacion y el checkout dan el MISMO total
    Given un carrito cotizado
    When coloco la orden con ese mismo carrito y nada ha cambiado
    Then el total de la orden es identico al cotizado
    # El desglose lo calcula UNA sola pieza. Dos calculos separados divergen, y ese es
    # exactamente el bug que se viene a evitar.

  Scenario: La cotizacion no obliga a nada
    Given un carrito cotizado con un precio
    And el administrador cambia ese precio antes de que yo compre
    When coloco la orden
    Then la orden se congela con el precio NUEVO
    # Manda el checkout, no la cotizacion.

  # --- Mover la orden por su ciclo de vida -------------------------------------

  Scenario: Solo un administrador mueve una orden
    Given una orden pagada
    When su comprador intenta pasarla a "shipped"
    Then recibe 403

  Scenario: El camino de preparacion
    Given una orden en "paid"
    When el administrador la pasa a "preparing"
    Then queda en "preparing"
    And desde ahi puede pasar a "shipped" y luego a "delivered"

  Scenario: Una orden sin pagar no se puede preparar
    Given una orden en "placed"
    When el administrador intenta pasarla a "preparing"
    Then recibe 409 con el codigo "invalid_transition"
    # Preparar un pedido que nadie ha pagado es enviar mercancia gratis.

  Scenario: No se puede saltar un paso
    Given una orden en "paid"
    When el administrador intenta pasarla a "delivered"
    Then recibe 409 con el codigo "invalid_transition"

  Scenario: Repetir la misma transicion es inofensivo
    Given una orden ya en "shipped"
    When el administrador la pasa a "shipped" otra vez
    Then recibe 204 y nada cambia
    # La transicion es un UPDATE condicional: reenviarla no es un error, es un duplicado.
    # No hace falta Idempotency-Key para esto.

  Scenario: Una orden entregada es final
    Given una orden en "delivered"
    When el administrador intenta moverla
    Then recibe 409 con el codigo "invalid_transition"

  Scenario: Mover una orden NO emite ningun evento todavia
    When el administrador pasa una orden a "shipped"
    Then no se encola nada en el outbox
    # Nadie consume order.shipped, y publicar sin cola que lo acepte vuelve como
    # 312 NO_ROUTE y agota el outbox en silencio.

  # --- Los contadores del panel ------------------------------------------------

  Scenario: Cada contexto publica SUS propios contadores
    Given un administrador
    Then puede pedir los contadores de ordenes, los del catalogo y los de usuarios
    And cada uno es un endpoint de su propio slice
    # Un solo /admin/dashboard obligaria a un slice a conocer a los otros tres.

  Scenario: Los contadores son solo para administradores
    Given un usuario autenticado sin rol admin
    When pide los contadores de cualquier contexto
    Then recibe 403

  Scenario: Stock bajo no incluye lo agotado
    Given un producto con stock 0 y otro con stock 3
    When pido los contadores del catalogo
    Then el agotado cuenta como "sin stock" y NO como "stock bajo"
    # Mezclarlos hace que el panel pida reponer lo que ya no se puede vender.
