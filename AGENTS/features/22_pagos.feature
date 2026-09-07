# language: en
Feature: Pagos
  Una orden nace SIN pagar. El cobro lo lleva un contexto acotado propio, contra una
  pasarela real, y la orden solo pasa a pagada cuando la pasarela lo confirma.

  Background:
    Given un comprador autenticado
    And una orden colocada con stock ya reservado

  # --- El ciclo de vida de la orden cambia -------------------------------------

  Scenario: Una orden recien colocada NO esta pagada
    When coloco una orden
    Then su estado es "placed"
    And su comprobante NO se genera todavia
    # Un comprobante de una compra que nadie ha pagado es un documento que miente.

  Scenario: La orden pasa a pagada cuando la pasarela lo confirma
    Given un pago capturado para mi orden
    When Ordering procesa el evento payment.captured
    Then la orden queda en "paid"
    And se emite order.paid
    And ese evento es el que dispara el comprobante

  # --- Elegir con que se paga --------------------------------------------------

  Scenario: El proveedor lo elige el comprador, no el despliegue
    When pido pagar con "stripe"
    Then se usa el adaptador de Stripe
    # Por eso esto es Strategy y no puerto elegido en el composition root: la
    # implementacion NO la decide la infraestructura, la decide cada peticion.

  Scenario: Anadir un proveedor nuevo no toca nada existente
    Given un adaptador nuevo que declara su Provider
    When se registra en el contenedor
    Then queda disponible sin modificar el registro ni el servicio

  Scenario: Un proveedor desconocido se rechaza diciendo cuales hay
    When pido pagar con "bitcoin"
    Then recibo 400
    And el mensaje enumera los proveedores disponibles

  Scenario: Sin ningun proveedor configurado la API arranca igual
    Given Payments:Stripe:SecretKey vacio
    Then la aplicacion arranca
    But POST /api/v1/payment devuelve 503
    # Degradacion explicita: cobrar no es una optimizacion, asi que NO se degrada
    # en abierto; falla ruidosamente y el resto de la API sigue en pie.

  # --- Iniciar un pago ---------------------------------------------------------

  Scenario: Iniciar un pago devuelve lo que el cliente necesita para confirmarlo
    When pido pagar mi orden
    Then recibo 201 con la referencia del pago y el clientSecret de la pasarela
    And el pago queda en "pending"

  Scenario: Reintentar con la misma Idempotency-Key no crea dos pagos
    When pido pagar mi orden dos veces con la misma clave
    Then recibo el mismo pago
    And en la pasarela solo se creo un intento

  Scenario: No se puede pagar la orden de otro
    When intento pagar una orden que no es mia
    Then recibo 404

  Scenario: No se paga dos veces una orden ya pagada
    Given mi orden ya esta pagada
    When pido pagar otra vez
    Then recibo 409

  # --- El webhook: la unica fuente de verdad del cobro -------------------------

  Scenario: Un webhook sin firma valida se rechaza
    When llega un webhook con una firma que no cuadra
    Then recibo 400
    And no cambia ningun pago
    # El cuerpo lo manda cualquiera que conozca la URL: sin verificar la firma,
    # marcar un pago como cobrado es gratis para un atacante.

  Scenario: Un webhook fuera de plazo se rechaza
    When llega un webhook correctamente firmado pero con marca de tiempo vieja
    Then recibo 400
    # Sin ventana temporal, una firma capturada se puede reenviar para siempre.

  Scenario: El mismo evento dos veces solo se procesa una
    When la pasarela reenvia el mismo evento
    Then el pago cambia una sola vez
    And se emite un solo payment.captured
    # Las pasarelas reintentan por diseno: el webhook es at-least-once.

  Scenario: Un evento de un pago que no conocemos no rompe nada
    When llega un webhook de un pago que no esta en la base
    Then respondo 200
    # Devolver error haria que la pasarela reintentara para siempre algo que
    # nunca va a existir.

  Scenario: Un pago fallido deja la orden sin pagar
    When llega un webhook de pago fallido
    Then el pago queda en "failed"
    And la orden sigue en "placed"

  # --- Consultar ---------------------------------------------------------------

  Scenario: Consulto mis pagos
    When pido GET /api/v1/payment/paged
    Then salen solo los mios

  Scenario: Un admin ve los de todos
    When un admin pide GET /api/v1/payment/all
    Then salen los de todos los compradores

  # --- La consecuencia de que la orden nazca sin pagar --------------------------

  Scenario: Una orden que nadie paga devuelve su stock
    Given una orden en "placed" mas vieja que la ventana de reserva
    And ningun pago capturado para ella
    When pasa el recolector de ordenes abandonadas
    Then la orden queda en "cancelled"
    And el stock de sus lineas vuelve al catalogo
    # Sin esto, reservar stock al colocar la orden lo pierde para siempre en cuanto
    # alguien abandona el carrito: es la deuda que crea mover Paid mas adelante.

  Scenario: El recolector no toca una orden pagada
    Given una orden en "paid"
    When pasa el recolector
    Then no la cambia

  Scenario: El recolector no toca una orden dentro de su ventana
    Given una orden en "placed" colocada hace un minuto
    When pasa el recolector
    Then no la cambia

  Scenario: Devolver el stock es idempotente
    When el recolector procesa dos veces la misma orden
    Then el stock vuelve una sola vez
    # El cambio de estado y la devolucion van en la misma transaccion, y solo la
    # transicion placed -> cancelled autoriza a devolver.
