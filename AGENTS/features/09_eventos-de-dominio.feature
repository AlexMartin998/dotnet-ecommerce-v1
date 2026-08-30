# ⚠️ PARCIALMENTE VERIFICADO. El outbox está probado; el camino del broker NO se ha
# ejercitado nunca (no hay RabbitMQ ni Docker en el entorno de trabajo). Los escenarios
# marcados con @broker están PENDIENTES de ejecutar. Ver AGENTS/planning/09.

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

  @broker @pendiente
  Scenario: El evento llega a la cola cuando el broker vuelve
    Given hay eventos pendientes en el outbox
    When RabbitMQ vuelve a estar disponible
    Then el publicador los publica y los marca como procesados
    And el mensaje viaja como persistente y con "MessageId"

  @broker @pendiente
  Scenario: El consumidor aplica el efecto y confirma
    When llega un evento "product.purchased" con stock restante 2
    Then el consumidor registra el aviso de stock bajo
    And hace ack del mensaje
    And queda una fila en "ProcessedMessages"

  # El outbox garantiza at-least-once: el mensaje PUEDE llegar dos veces.
  @broker @pendiente
  Scenario: Un mensaje duplicado no repite el efecto
    Given el mensaje "M" ya fue procesado
    When el broker vuelve a entregar "M"
    Then el efecto no se aplica de nuevo
    And se hace ack sin error

  # La marca se escribe ANTES del efecto: al revés, la clave primaria solo arbitraría
  # qué fila sobrevive, y el efecto se habría aplicado dos veces.
  @broker @pendiente
  Scenario: Dos réplicas procesando el mismo mensaje
    When dos consumidores procesan "M" a la vez
    Then el efecto se aplica exactamente una vez

  @broker @pendiente
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
  @broker @pendiente
  Scenario: Un fallo transitorio no confirma el mensaje
    When el procesado falla por un deadlock de la base
    Then NO se hace ack
    And el mensaje se reintenta o va a la DLQ, pero no se pierde
