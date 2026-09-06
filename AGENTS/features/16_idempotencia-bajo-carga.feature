Feature: La idempotencia aguanta bajo carga
  Como dueño de la API
  quiero que "Idempotency-Key" siga garantizando exactamente-una-vez cuando hay carga
  porque es justo cuando la API va lenta cuando el cliente reintenta, y por tanto
  cuando la garantia mas falta hace

  # Contexto medido el 2026-09-06 contra SQL Server, Redis y RabbitMQ reales
  # (192.168.3.82). Lo que se descubrio: el mecanismo es CORRECTO, pero se APAGA SOLO
  # bajo carga. Con AsyncTimeout de 1000 ms, un unico multiplexer y 3 operaciones a
  # Redis por peticion, una rafaga hace que el `SET NX` agote el timeout; el store
  # falla en abierto y la peticion se ejecuta SIN garantia. Redis estaba sano.
  # Medido: 26 de 1000 peticiones (2,6 %) a 64 conexiones, y 229 en una tanda de ~1040.

  Background:
    Given Redis esta disponible y sano
    And tengo un token de un usuario autenticado

  # --- Lo que ya funcionaba y no se puede romper --------------------------------

  Scenario: Exactamente-una-vez con una rafaga sobre un solo proceso
    Given el producto "SKU-L" tiene stock 500
    When 350 clientes compran 1 unidad a la vez con la misma "Idempotency-Key"
    Then el stock baja exactamente 1
    And exactamente una respuesta no viene reproducida

  Scenario: Exactamente-una-vez repartido entre dos replicas
    Given hay dos instancias de la API contra el mismo Redis
    And el producto "SKU-R" tiene stock 400
    When 80 clientes compran 1 unidad a la vez con la misma "Idempotency-Key",
      alternando entre las dos instancias
    Then el stock baja exactamente 1

  # --- Lo que hay que arreglar ---------------------------------------------------

  # El coste de Redis por peticion es lo que provoca el timeout que apaga la garantia.
  # `SET clave valor EX ttl NX GET` (Redis >= 7.0) reserva y lee en UN viaje: hace
  # innecesario el GET previo, que ademas era la mitad de la carrera de mas abajo.
  Scenario: Una peticion idempotente no cuesta mas de dos viajes a Redis
    Given el producto "SKU-O" tiene stock 500
    When compro 400 veces con claves distintas
    Then el numero de operaciones a Redis por peticion no pasa de 2
    # Linea base medida antes del cambio: 3,00 (get + set + setex/unlink)

  # El agujero: el camino que reproduce una respuesta "recien terminada" NO comparaba
  # la huella del cuerpo. Es la misma falla silenciosa que el 422 vino a cerrar,
  # alcanzable por carrera en vez de en secuencia.
  Scenario: El 422 protege TAMBIEN a quien pierde la reserva por poco
    Given una peticion con la clave "K" y cantidad 1 esta en curso
    When llega otra peticion con la clave "K" y cantidad 5
    Then recibe 422 con code "idempotency_key_reuse"
    And nunca recibe la respuesta de la primera

  # Sin limite, la clave la elige el cliente y acaba entera dentro de una clave de
  # Redis que vive 24 h. Medido: se aceptaban claves de 7000 caracteres.
  Scenario: Una clave desmedida se rechaza
    When compro con una "Idempotency-Key" de 7000 caracteres
    Then recibo 400 con code "idempotency_key_invalid"
    And la operacion no se ejecuta

  # Regla 5 del proyecto: toda seccion se enlaza a una clase tipada y validada. Ademas
  # sin esto la expiracion de la reserva NO se puede probar (60 s clavados en el codigo).
  Scenario: Los plazos son configuracion, no constantes
    Given la seccion "Idempotency" fija una reserva de 2 segundos
    When arranca la aplicacion
    Then la reserva de una operacion en curso caduca a los 2 segundos

  # La deuda que ya estaba anotada (planning/12 §12.5): la reserva es un lease sin
  # renovacion. Se documenta con un test que la fija, no se disimula.
  Scenario: Una reserva caducada deja pasar una segunda ejecucion
    Given la reserva dura 2 segundos
    And una peticion con la clave "K" tarda mas que eso
    When llega una segunda peticion con la clave "K" pasados esos 2 segundos
    Then la segunda se ejecuta de verdad
    And queda constancia de que la garantia no se aplico

  # El hallazgo principal. La decision de "degradar en abierto" se tomo para el caso
  # "Redis caido"; un timeout con Redis VIVO es otra cosa y hoy es indistinguible.
  Scenario: Ejecutar sin garantia deja rastro
    Given el store de idempotencia no responde a tiempo
    When se ejecuta una operacion protegida por "Idempotency-Key"
    Then la operacion se ejecuta igual, sin 500
    And la respuesta avisa de que la garantia no se aplico
    And el contador de peticiones sin garantia sube
