Feature: La mensajeria aguanta lo que le pasa de verdad
  Como dueño de la API
  quiero que el consumidor no pierda mensajes, se pueda operar y se pueda probar
  porque el at-least-once del broker solo sirve si el consumidor cumple su mitad

  # Cierra planning/12 §12.5. Casi todo viene del mismo sitio: piezas que vivian
  # DENTRO de un BackgroundService atado a AMQP y por eso no se podian probar.

  Background:
    Given hay un broker configurado

  # --- El P0 que llevaba sin test desde que se arreglo ----------------------------

  # El bug: la marca de "ya procesado" se confirmaba ANTES del efecto, asi que si el
  # efecto fallaba, la reentrega se tomaba por duplicado, se hacia ack y el mensaje
  # DESAPARECIA sin procesarse. Los reintentos eran inertes para su unico caso de uso.
  Scenario: Un efecto que falla no deja marca
    Given un mensaje sin procesar
    When el efecto lanza una excepcion
    Then no queda marca de procesado
    And el mensaje sigue estando pendiente

  Scenario: Tras el fallo, el reintento SI vuelve a ejecutar
    Given un mensaje cuyo efecto fallo
    When llega otra vez
    Then el efecto se ejecuta de nuevo
    And esta vez queda marcado

  Scenario: Una reentrega de algo ya procesado no repite el efecto
    Given un mensaje ya procesado
    When el broker lo reentrega
    Then el efecto no se ejecuta
    And se confirma igualmente

  Scenario: Dos replicas a la vez, un solo efecto
    Given dos instancias procesando el mismo mensaje simultaneamente
    Then solo una aplica el efecto
    And la que pierde falla con el choque de clave primaria
    And esa excepcion es reconocible como duplicado
    # De eso depende el consumidor para hacer ack en vez de gastar un reintento.

  # --- Operar la cola: lo que un humano necesita poder hacer ----------------------

  # x-death sobrevive al paso por la DLQ, asi que un mensaje reencolado volvia con el
  # contador agotado y moria en la primera entrega: la herramienta que existe para
  # recuperar mensajes no los recuperaba.
  Scenario: Reencolar desde la DLQ devuelve el presupuesto completo
    Given un mensaje en la DLQ con el contador agotado
    When un operador borra la cabecera "x-retry-attempt" y lo reencola
    Then vuelve a tener todos sus intentos

  Scenario: El contador se entiende aunque lo escriba un humano
    Given una cabecera "x-retry-attempt" puesta a mano desde la UI del broker
    Then se lee igual venga como entero, como texto o como bytes
    # ⚠️ El error que ya se cometio con x-death: los textos viajan como byte[] y
    # compararlos sin convertir devuelve false EN SILENCIO.

  # --- Cambiar la configuracion sin parar nada ------------------------------------

  # El TTL vive en x-message-ttl, que se fija al declarar la cola: cambiarlo daba
  # 406 PRECONDITION_FAILED y dejaba la mensajeria abajo.
  Scenario: Cambiar el plazo de reintento no rompe el arranque
    Given la cola de espera existe con un plazo de 7 segundos
    When se despliega con "RabbitMq:RetryDelaySeconds" a 12
    Then la aplicacion arranca sin error
    And se declara una cola de espera NUEVA con el plazo nuevo
    And la vieja se queda vacia y sin recibir nada

  # ⚠️ Encontrado EJECUTANDO: con un exchange de por medio, cada reintento se copiaba
  # a TODAS las colas de espera ligadas a el -incluidas las de plazos anteriores-.
  Scenario: Un reintento entra en una sola cola de espera
    Given existen colas de espera de plazos anteriores
    When un mensaje falla y se programa su reintento
    Then aparece unicamente en la cola de espera vigente

  Scenario: El mensaje que espera vuelve solo a la cola principal
    Given un mensaje en la cola de espera
    When pasa su plazo
    Then el broker lo devuelve a la cola principal
    And se procesa exactamente una vez

  # --- Coste ----------------------------------------------------------------------

  Scenario: Publicar una tanda no abre un canal por mensaje
    When el outbox publica 30 eventos
    Then se usa un unico canal AMQP
    # Abrir un canal es un viaje de ida y vuelta al broker; con BatchSize 50 eran 50.

  # --- Y lo que se decide NO hacer -------------------------------------------------

  Scenario: El orden de publicacion del outbox no se garantiza
    Given dos eventos de agregados distintos
    Then pueden publicarse en un orden distinto al de su columna Sequence
    # Es determinista y repetible, pero NO ordenado: el IDENTITY se asigna al INSERT y
    # la fila se ve al COMMIT. Se acepta a proposito. Señal para reabrirlo: el dia que
    # un consumidor necesite ver dos eventos del MISMO agregado en orden.
