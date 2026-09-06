Feature: Ordenes y su comprobante en PDF
  Como comprador
  quiero que mi compra quede registrada como una orden y poder descargar su comprobante
  porque hoy comprar descuenta stock y no deja ni rastro de QUE se compro

  # Alcance fijado con el owner el 2026-09-06: orden de UN SOLO vendedor. La imagen de
  # referencia era un marketplace con sub-ordenes por seller; aqui no hay sellers.
  # Lo que si se toma de ella: la FORMA del documento -items, totales desglosados,
  # cliente, direccion de envio y una linea de tiempo de estados-.
  #
  # Y lo importante: el PDF NO se genera dentro de la peticion. Se emite un evento de
  # dominio por el outbox que YA existe, y un consumidor lo genera aparte.

  Background:
    Given tengo un token de un usuario autenticado

  # --- La orden ----------------------------------------------------------------

  Scenario: Comprar crea una orden con su numero legible
    Given hay productos con stock
    When hago una compra de 2 lineas
    Then se crea una orden con numero "ORD-<año>-<secuencia>"
    And la orden guarda el precio y el nombre del producto EN EL MOMENTO de comprar
    # ⚠️ Copiados, no referenciados: si mañana sube el precio o se renombra el producto,
    # el comprobante de ayer tiene que seguir diciendo lo que se cobro de verdad.

  Scenario: El stock se descuenta en la misma transaccion que la orden
    When compro 3 unidades
    Then el stock baja 3 y la orden queda creada
    And si algo falla, no queda ni lo uno ni lo otro

  Scenario: Sin stock no hay orden
    Given un producto con stock 1
    When intento comprar 5
    Then recibo 409
    And no se crea ninguna orden

  Scenario: Reintentar la misma compra no crea dos ordenes
    When repito la peticion con la misma "Idempotency-Key"
    Then recibo la misma orden
    And solo existe una
    # Reutiliza la garantia transaccional de planning/17: no hay mecanismo nuevo.

  Scenario: Cada quien ve solo sus ordenes
    Given otro usuario tiene una orden
    When intento verla
    Then recibo 404
    # 404 y no 403: decir "existe pero no es tuya" ya filtra que existe.

  # --- El comprobante, ASINCRONO ------------------------------------------------

  # El PDF no se genera en la peticion: tarda, y una compra no puede depender de que
  # el generador este vivo. Se emite un evento y se genera aparte.
  Scenario: Comprar no espera al PDF
    When hago una compra
    Then la respuesta llega sin esperar a que exista el comprobante
    And la orden queda con el comprobante "pendiente"

  Scenario: El consumidor genera el comprobante y lo deja disponible
    Given una orden recien creada
    When el consumidor procesa su evento
    Then el comprobante se guarda en el almacen de documentos
    And la orden pasa a "disponible" con la referencia del documento

  Scenario: Pedir un comprobante que aun no esta
    Given una orden cuyo comprobante todavia se esta generando
    When pido descargarlo
    Then recibo 409 con code "receipt_not_ready"
    And puedo reintentar

  Scenario: Generar el comprobante dos veces no duplica nada
    Given el broker reentrega el evento
    Then el comprobante no se genera otra vez
    # Reutiliza IMessageInbox: marca y efecto en la misma transaccion.

  Scenario: Si el generador falla, se reintenta
    Given la generacion del PDF falla
    Then el mensaje se reintenta con su espera
    And la orden sigue existiendo y la compra sigue siendo valida
    # Que no se pueda imprimir un papel no puede invalidar una compra ya cobrada.

  # --- El almacen: cambiar de infra sin tocar el codigo --------------------------

  Scenario: Hoy es el sistema de ficheros
    When se guarda un comprobante
    Then acaba en el disco, FUERA de wwwroot
    # Dentro de wwwroot lo serviria UseStaticFiles a cualquiera que adivine la ruta.

  Scenario: Manana es S3, R2, MinIO o Cloudinary
    Given una implementacion distinta de la misma interfaz
    When se registra en el composition root
    Then ni el consumidor ni el controller ni el dominio cambian una linea

  Scenario: La base NO guarda una ruta del disco
    When se guarda un comprobante
    Then lo que se persiste es una clave OPACA del almacen
    # Guardar "/app/documents/2026/09/x.pdf" ata la base a la infraestructura de hoy:
  # al migrar a S3 habria que reescribir todas las filas.

  # --- El acceso: que no se pueda adivinar --------------------------------------

  Scenario: El comprobante se sirve por un endpoint, no como estatico
    When descargo el comprobante de mi orden
    Then llega el PDF
    And no existe ninguna URL publica desde la que pudiera haberlo adivinado

  Scenario: El comprobante de otro no se descarga
    Given una orden que no es mia
    When intento descargar su comprobante
    Then recibo 404

  Scenario: Sin autenticar no hay comprobante
    When pido un comprobante sin token
    Then recibo 401
