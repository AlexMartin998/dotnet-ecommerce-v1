Feature: Idempotencia de peticiones
  Como cliente móvil con red inestable
  quiero poder reintentar un POST sin duplicar su efecto
  porque no sé si el servidor llegó a procesarlo

  # Resuelve lo que NI RowVersion NI el índice único cubren: para la base, dos compras
  # son dos operaciones legítimamente distintas. No hay conflicto que detectar.

  Background:
    Given Redis está disponible
    And tengo un token de un usuario autenticado
    And el producto "SKU-I" tiene stock 10

  Scenario: Sin cabecera, cada petición se ejecuta
    When compro 2 unidades tres veces sin "Idempotency-Key"
    Then el stock queda en 4

  Scenario: Con la misma clave, solo la primera se ejecuta
    When compro 2 unidades cinco veces con la misma "Idempotency-Key"
    Then el stock queda en 8
    And las cuatro últimas respuestas traen "Idempotency-Replayed: true"
    And todas devuelven el mismo cuerpo y el mismo código

  Scenario: Peticiones simultáneas con la misma clave
    When 6 clientes compran 3 unidades a la vez con la misma "Idempotency-Key"
    Then exactamente 1 recibe 200
    And el resto recibe 409 con code "idempotency_in_progress"
    And el stock refleja UNA sola compra

  # Memorizar un error convertiría un fallo transitorio en permanente durante 24 h.
  Scenario: Un fallo libera la clave para poder reintentar de verdad
    Given compro con clave "K" un SKU que no existe
    And recibo 404
    When repito la compra con la clave "K" y un SKU válido
    Then la operación se ejecuta y recibo 200

  # Sin usuario en la clave, dos clientes con el mismo GUID se reproducen la respuesta
  # el uno al otro: fuga de datos entre cuentas.
  Scenario: La clave está aislada por usuario
    Given el usuario A compró con la clave "K"
    When el usuario B usa la misma clave "K"
    Then la petición de B se ejecuta y NO recibe la respuesta de A

  # Con un solo TTL de 24 h, morir entre reservar y guardar bloqueaba la clave un día
  # entero por una operación que nunca llegó a ejecutarse.
  Scenario: Una reserva huérfana expira pronto
    Given una petición reservó la clave y el proceso murió antes de responder
    When reintento pasado el TTL de reserva
    Then la operación se ejecuta normalmente

  Scenario: El replay conserva las cabeceras de la respuesta original
    Given un endpoint idempotente que devuelve 201 con "Location"
    When repito la petición con la misma clave
    Then recibo 201 con la misma cabecera "Location"

  # Un corte de Redis justo después del commit devolvía 500 por una compra ya cobrada:
  # el mecanismo antidoble-cobro provocándolo.
  Scenario: Con Redis caído la compra no falla
    Given Redis no está disponible
    When compro con "Idempotency-Key"
    Then recibo 200
    And la operación se ejecuta sin garantía de idempotencia
