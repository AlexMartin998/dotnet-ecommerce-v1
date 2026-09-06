Feature: La idempotencia es una garantia, no una optimizacion
  Como dueño de la API
  quiero que "no ejecutar dos veces" no dependa de una cache
  porque una garantia que se apaga cuando hay carga no es una garantia

  # Viene de features/16, que midio el problema: bajo carga el almacen de Redis se
  # apagaba solo y la compra se ejecutaba sin proteccion (174 de 14 400, Redis SANO).
  # La respuesta NO es elegir entre degradar en abierto o devolver 503: es mover la
  # marca a la MISMA transaccion que el efecto, y entonces la pregunta desaparece.
  #
  # Es el "inbox pattern" que documenta Azure, es donde Stripe guarda sus claves (su
  # misma base de negocio, no una cache), y es lo que este repo YA hacia bien en
  # ProductPurchasedConsumer con ProcessedMessages.

  Background:
    Given tengo un token de un usuario autenticado
    And el producto "SKU-T" tiene stock 10

  # --- La garantia, que ya no depende de Redis -----------------------------------

  Scenario: El reintento no vuelve a comprar
    When compro 3 unidades dos veces con la misma "Idempotency-Key"
    Then el stock queda en 7
    And la segunda respuesta trae "Idempotency-Replayed: true"
    And las dos respuestas son identicas byte a byte

  # ⭐ Con el almacen en Redis esto era IMPOSIBLE de cumplir: sin Redis no habia
  # idempotencia. Ahora "no hay almacen" y "no hay compra" son el mismo evento.
  Scenario: La garantia sigue en pie con Redis caido
    Given Redis no esta disponible
    When compro 3 unidades dos veces con la misma "Idempotency-Key"
    Then el stock queda en 7
    And la segunda respuesta trae "Idempotency-Replayed: true"

  Scenario: Simultaneas con Redis caido
    Given Redis no esta disponible
    When 6 clientes compran 2 unidades a la vez con la misma "Idempotency-Key"
    Then todas reciben 200
    And exactamente una NO viene marcada como reproducida
    And el stock refleja UNA sola compra
    # Sin puerta de admision no hay 409: quien arbitra es la clave primaria. El que
    # pierde se bloquea en la clave, choca, su transaccion entera se deshace -incluido
    # el descuento de stock- y devuelve el resultado del ganador.

  Scenario: Reusar la clave con otro cuerpo, con Redis caido
    Given Redis no esta disponible
    And compre 2 unidades con la clave "K"
    When compro 5 unidades con la clave "K"
    Then recibo 422 con code "idempotency_key_reuse"
    And el stock refleja solo la primera compra

  # --- La garantia es del SERVICIO, no del atributo -------------------------------

  # El mismo bug que motivo mover la transaccion: llamar a BuyAsync desde un job perdia
  # la garantia en silencio. El compilador ya no lo permite.
  Scenario: Ejecutar la compra exige declarar la intencion
    Given un llamador nuevo de "BuyAsync"
    When no declara ninguna intencion
    Then el codigo no compila
    And renunciar a la proteccion tiene que escribirse como "CommandIntent.None"

  # --- Redis se queda con lo que si le corresponde --------------------------------

  Scenario: La puerta frena la tormenta antes de que llegue a la base
    Given una peticion con la clave "K" esta en vuelo
    When llegan 300 peticiones mas con la clave "K"
    Then reciben 409 con code "idempotency_in_progress"
    And ninguna de ellas abre una transaccion
    # Sin la puerta serian correctas igual, pero las 300 se quedarian bloqueadas en la
    # clave primaria reteniendo cada una su conexion a la base.

  Scenario: Que la puerta falle no rompe nada
    Given el almacen de la puerta no responde
    When compro con "Idempotency-Key"
    Then la operacion se ejecuta y sigue estando protegida
    And el contador "gate_unavailable" sube
    # Ya no hay cabecera "Idempotency-Guaranteed: false": seria mentira. La garantia
    # no depende de la puerta.

  # --- La retencion, que es lo unico que sigue siendo una eleccion ----------------

  Scenario: Los comandos ejecutados caducan con el resto de tablas
    Given un comando ejecutado hace mas de "Outbox:RetentionDays" dias
    When pasa el recolector
    Then su fila se borra
    # ⚠️ Ese plazo tiene que cubrir el PEOR reintento de un cliente: a partir de ahi la
    # misma clave vuelve a ejecutar de verdad.
