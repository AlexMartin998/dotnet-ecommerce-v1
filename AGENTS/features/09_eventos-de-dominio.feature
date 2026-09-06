# ✅ VERIFICADO de punta a punta el 2026-09-05 contra un RabbitMQ real (rabbitmq_generic,
# 3.13-management). Los escenarios @broker dejan de estar pendientes: se ejercitaron
# publicación, consumo, deduplicación y DLQ. Ver AGENTS/planning/09.
#
# La verificación destapó un bug REAL en la contabilidad de reintentos, medido y
# corregido: ver el escenario "Una caída del broker no entierra eventos".

Feature: Eventos de dominio con outbox transaccional
  Como sistema
  quiero publicar lo que pasa sin atar la operación de negocio a que el broker esté vivo
  y sin anunciar nunca algo que no llegó a confirmarse

  # No se puede escribir en la BD y en el broker atómicamente:
  #   publicar y luego commit -> anuncias una compra que NO existe
  #   commit y luego publicar -> la compra existe y nadie se entera
  # El outbox lo resuelve: el evento es una fila más de la misma transacción.

  Scenario: Una compra deja el evento en el outbox
    Given el producto "SKU-E" tiene stock 7
    When compro 1 unidad
    Then el stock queda en 6
    And existe una fila en "OutboxMessages" de tipo "product.purchased" sin procesar
    And el payload trae SKU, cantidad, stock restante y el id del comprador

  Scenario: El evento y el descuento son atómicos
    Given la escritura del outbox va a fallar
    When compro 1 unidad
    Then la compra NO se confirma
    And el stock no cambia

  Scenario: La API funciona con el broker caído
    Given RabbitMQ no está disponible
    When compro 3 veces
    Then las 3 reciben 200
    And los 3 eventos quedan en el outbox reintentándose

  Scenario: Sin broker configurado la aplicación arranca igual
    Given "RabbitMq:ConnectionString" está vacío
    When arranco la aplicación
    Then arranca correctamente
    And el publicador y el consumidor quedan desactivados
    And las compras siguen escribiendo en el outbox

  Scenario: Los eventos agotados se anuncian en la sonda de salud
    Given hay eventos que agotaron sus reintentos
    When pido "GET /health/ready"
    Then el estado es "Degraded"
    And el detalle indica cuántos necesitan revisión manual

  @broker
  Scenario: El evento llega a la cola cuando el broker vuelve
    Given hay eventos pendientes en el outbox
    When RabbitMQ vuelve a estar disponible
    Then el publicador los publica y los marca como procesados
    And el mensaje viaja como persistente y con "MessageId"

  @broker
  Scenario: El consumidor aplica el efecto y confirma
    When llega un evento "product.purchased" con stock restante 2
    Then el consumidor registra el aviso de stock bajo
    And hace ack del mensaje
    And queda una fila en "ProcessedMessages"

  # El outbox garantiza at-least-once: el mensaje PUEDE llegar dos veces.
  @broker
  Scenario: Un mensaje duplicado no repite el efecto
    Given el mensaje "M" ya fue procesado
    When el broker vuelve a entregar "M"
    Then el efecto no se aplica de nuevo
    And se hace ack sin error

  # La marca se escribe ANTES del efecto: al revés, la clave primaria solo arbitraría
  # qué fila sobrevive, y el efecto se habría aplicado dos veces.
  @broker
  Scenario: Dos réplicas procesando el mismo mensaje
    When dos consumidores procesan "M" a la vez
    Then el efecto se aplica exactamente una vez

  @broker
  Scenario Outline: Un mensaje que no se puede procesar acaba en la DLQ
    When llega <mensaje>
    Then se hace nack sin reencolar
    And el mensaje aparece en la dead-letter queue

    Examples:
      | mensaje                        |
      | un cuerpo que no deserializa   |
      | un mensaje sin "MessageId"     |
      | un tipo de evento inesperado   |

  # Un catch (DbUpdateException) a secas se tragaba deadlocks, hacía ack, y el mensaje
  # desaparecía de la cola SIN procesarse.
  @broker
  Scenario: Un fallo transitorio no confirma el mensaje
    When el procesado falla por un deadlock de la base
    Then NO se hace ack
    And el mensaje se reintenta o va a la DLQ, pero no se pierde

  # ⚠️ BUG REAL, medido el 2026-09-05. `Attempts` contaba igual "este mensaje falla" y
  # "el broker esta caido". Con 5 intentos cada 5 s, **25 segundos** de broker caido
  # dejaban el evento en Attempts=5, fuera del filtro del publicador y por tanto sin
  # republicarse nunca — ni al volver el broker. Menos de lo que tarda en arrancar el
  # propio contenedor de RabbitMQ (start_period: 30s).
  @broker
  Scenario: Una caída del broker no entierra eventos
    Given RabbitMQ no está disponible
    When compro 1 unidad
    And el publicador reintenta durante más tiempo del que dura el máximo de intentos
    Then el evento sigue con 0 intentos consumidos
    And el log dice que ningún intento se consumió
    When RabbitMQ vuelve a estar disponible
    Then el evento se publica y se marca como procesado

  # Solo el fallo atribuible al mensaje gasta intentos, y no bloquea a los demás
  # de la tanda: con `break` un mensaje envenenado retrasaba a los que sí saldrían.
  @broker
  Scenario: Un mensaje que el broker rechaza sí agota sus intentos
    Given el broker está disponible
    And un evento cuya publicación falla por sí misma
    When el publicador lo intenta "MaxPublishAttempts" veces
    Then el evento queda para revisión manual
    And los demás eventos de la tanda se publican igual
    And "GET /health/ready" pasa a "Degraded"
