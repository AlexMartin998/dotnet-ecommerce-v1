Feature: Límites de tasa, sondas de salud y arranque
  Como operador
  quiero que la API se defienda del abuso y diga la verdad sobre su estado
  para poder ponerla detrás de un orquestador sin sorpresas

  # El lockout de Identity cuenta fallos POR USUARIO; sin límite por IP se sortea
  # probando contraseñas contra MUCHOS usuarios (password spraying).
  Scenario: Los endpoints de auth tienen una ventana estricta
    When hago 12 peticiones de login en menos de un minuto desde la misma IP
    Then las primeras 10 responden normalmente
    And el resto recibe 429

  Scenario: El resto de la API tiene un límite global por IP
    When supero 100 peticiones por minuto desde la misma IP
    Then recibo 429

  # Detrás de un proxy, sin UseForwardedHeaders el limitador particiona por la IP DEL
  # PROXY: un solo cubo de 100 req/min para todo internet.
  Scenario: Detrás de un proxy se particiona por la IP real del cliente
    Given la petición llega con "X-Forwarded-For"
    When dos clientes distintos hacen peticiones
    Then consumen cuotas independientes

  Scenario: La sonda de vida no depende de nada externo
    Given SQL Server no está disponible
    When pido "GET /health"
    Then recibo 200
    # Si la sonda de VIDA dependiera de la base, una caída de la base haría que el
    # orquestador reiniciara procesos perfectamente sanos.

  Scenario: La sonda de preparación sí comprueba las dependencias
    When pido "GET /health/ready"
    Then el resultado refleja el estado de SQL Server, de Redis y del backlog del outbox

  Scenario: La sonda de preparación no filtra topología
    When pido "GET /health/ready" sin autenticarme
    Then el cuerpo es solo el estado agregado, sin detalle de las dependencias

  # P0 real: [Required] sobre AdminPassword se validaba antes de mirar Seed:Enabled,
  # así que un despliegue con el seeding apagado moría en crash-loop.
  Scenario: Arranca en Production con el seeding apagado y sin contraseña de admin
    Given "ASPNETCORE_ENVIRONMENT" es "Production"
    And "Seed:Enabled" es false
    And no se define "Seed:AdminPassword"
    When arranco la aplicación
    Then arranca correctamente

  Scenario: Con el seeding encendido sin contraseña, falla al ARRANCAR
    Given "Seed:Enabled" es true
    And no se define "Seed:AdminPassword"
    When arranco la aplicación
    Then el arranque falla con un mensaje que nombra "Seed:AdminPassword"

  Scenario: Un secreto JWT ausente o corto impide el arranque
    Given "Jwt:SecretKey" está vacío
    When arranco la aplicación
    Then el arranque falla
    # ValidateOnStart: si no, el fallo aparece en el primer login, en producción, de noche.

  # La imagen de runtime no lleva SDK ni dotnet-ef, así que nadie más puede aplicarlas.
  Scenario: Las migraciones se aplican al arrancar
    Given la base está vacía
    When arranco la aplicación
    Then el esquema queda creado
    And los endpoints responden sin errores de tabla inexistente

  # La configuración de .NET FUSIONA arrays por índice, no los reemplaza.
  Scenario: Los orígenes CORS de un entorno no heredan los de otro
    Given "appsettings.json" define la lista de orígenes vacía
    And el entorno define "Cors__AllowedOrigins__0"
    When consulto la política efectiva
    Then solo está permitido ese origen
