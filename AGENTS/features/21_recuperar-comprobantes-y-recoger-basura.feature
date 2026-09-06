Feature: Recuperar lo que murio en la DLQ, y recoger la basura del almacen
  Como operador del sistema
  quiero poder reemitir un comprobante que agoto sus reintentos y que el almacen no crezca
  con ficheros que nadie referencia
  porque hoy "failed" es un callejon sin salida: el cliente deja de esperar, pero nadie
  puede darle su documento sin entrar al broker a mano

  # Deuda que dejo abierta planning/20 (§20.6 y §20.10). Son dos mitades del mismo
  # problema: que un efecto pueda fallar sin que quede ni trabajo perdido ni basura.
  #
  # ⚠️ Lo que NO es: un reintento automatico infinito. Si un mensaje agoto sus intentos es
  # porque algo estaba roto de verdad; reencolarlo solo lo repite. Reemitir es una decision
  # HUMANA, y por eso es un endpoint de administracion y no un job.

  Background:
    Given soy administrador

  # --- La DLQ deja de ser un agujero -------------------------------------------

  Scenario: Ver cuanto hay muerto, y donde
    When consulto las dead-letters
    Then veo una fila por cola registrada con cuantos mensajes tiene parada
    And no necesito entrar a la consola del broker para saberlo

  Scenario: Reemitir un comprobante que fallo
    Given una orden cuyo comprobante agoto sus reintentos y quedo en "failed"
    When reencolo su cola de dead-letters
    Then el mensaje vuelve a la cola principal
    And el consumidor lo procesa
    And la orden pasa a "disponible" con su comprobante descargable

  Scenario: El presupuesto de reintentos se reinicia al reemitir
    Given un mensaje que agoto sus 5 intentos
    When lo reemito
    Then vuelve con el contador a cero
    # Si no, la herramienta para recuperar mensajes no recupera nada: el mensaje
    # volveria con el presupuesto gastado y moriria en la primera entrega.

  Scenario: Reemitir no pierde el mensaje si algo falla a mitad
    When se reemite un mensaje
    Then primero se publica y solo despues se confirma en la dead-letter
    And morir entremedias provoca como mucho un duplicado, nunca una perdida
    # Misma regla que el reintento con espera: se prefiere duplicar a perder.

  Scenario: Reemitir algo que ya se proceso no lo ejecuta dos veces
    Given un mensaje que en realidad si llego a procesarse
    When lo reemito
    Then el inbox lo reconoce por su MessageId y no hace nada
    # Reutiliza IMessageInbox: no hay mecanismo nuevo.

  Scenario: No se puede reencolar una cola cualquiera
    When pido reemitir de una cola que este servicio no consume
    Then recibo 404
    # El nombre viaja en la peticion, asi que la lista de colas registradas es una
    # allowlist por construccion. Sin eso, el endpoint mueve mensajes de CUALQUIER cola
    # del broker, incluidas las de otros proyectos que comparten el mismo.

  Scenario: Esto no lo toca cualquiera
    Given que no soy administrador
    When pido ver o reemitir dead-letters
    Then recibo 403

  Scenario: Sin broker, el endpoint lo dice
    Given que la mensajeria esta desactivada
    When consulto las dead-letters
    Then recibo 503 y no un error confuso

  # --- El almacen deja de acumular basura ---------------------------------------

  Scenario: Se borra lo que ninguna orden referencia
    Given un fichero en el almacen que ninguna orden apunta
    When corre el recolector
    Then el fichero se borra
    And el almacen deja de crecer con documentos inalcanzables

  Scenario: NUNCA se borra un comprobante vivo
    Given una orden con su comprobante
    When corre el recolector
    Then el fichero sigue ahi
    # Es la unica propiedad que no se puede equivocar: borrar de mas es perder el
    # documento de un cliente. Ante la duda, no se borra.

  Scenario: Un fichero recien escrito esta a salvo aunque aun no lo referencie nadie
    Given un comprobante escrito hace un instante, cuya transaccion todavia no confirmo
    When corre el recolector
    Then el fichero NO se borra
    # ⚠️ EL caso peligroso. El PDF se escribe DENTRO de la transaccion, asi que entre que
    # existe el fichero y existe la fila que lo apunta hay una ventana. Sin un periodo de
    # gracia, el recolector borraria comprobantes buenos a mitad de vuelo.

  Scenario: Un PDF truncado tambien se recoge
    Given una escritura que fallo a mitad y dejo un fichero incompleto
    When corre el recolector
    Then se borra como cualquier otro huerfano
    # No hace falta distinguirlo: como SaveAsync nunca devolvio clave, nadie lo referencia.

  Scenario: El recolector no puede tumbar la API
    Given que el almacen falla al listar
    Then el job lo registra y sigue vivo
    # Un BackgroundService que lanza muere y no vuelve, y desde .NET 6 se lleva el host
    # por delante.
