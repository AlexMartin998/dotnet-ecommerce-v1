Feature: Subida de imágenes de producto
  Como administrador
  quiero subir la imagen de un producto
  sin que eso rompa a los clientes JSON existentes ni abra un agujero de seguridad

  # Endpoint APARTE y no un campo del PATCH: el curso cambió POST/PUT de [FromBody] a
  # [FromForm] y rompió a todos sus clientes JSON con un breaking change no versionado.

  Background:
    Given tengo un token de administrador
    And existe el producto 1

  Scenario: Subida de una imagen válida
    When subo un PNG válido a "POST /api/v1/product/1/image"
    Then recibo 200
    And "imageUrl" es una ruta relativa bajo "/ProductsImages/"
    And la imagen se sirve por esa ruta con content-type "image/png"

  # Se persiste ruta RELATIVA: el curso guardaba {Request.Scheme}://{Request.Host}/...,
  # y Host es una cabecera que controla el CLIENTE (host header injection almacenada).
  Scenario: No se persiste una URL absoluta construida desde la petición
    When subo una imagen enviando una cabecera "Host" falsificada
    Then el valor guardado no contiene ese host

  # Extensión y content-type los pone el cliente y se pueden mentir los dos.
  # Estos archivos se sirven desde el MISMO ORIGEN que la API: un .svg o .html sería
  # XSS almacenado con las cookies de la API.
  Scenario Outline: Se rechaza lo que no sea una imagen de formato permitido
    When subo <archivo>
    Then recibo 400 con code "bad_request"

    Examples:
      | archivo                                      |
      | un .txt renombrado a .png                    |
      | un archivo con extensión .exe                |
      | un archivo con extensión .svg                |
      | un archivo vacío                             |
      | un archivo mayor que el límite configurado   |

  Scenario: El nombre del archivo lo genera el servidor
    When subo una imagen llamada "../../etc/passwd.png"
    Then el archivo guardado tiene un nombre generado (GUID) y vive bajo la carpeta gestionada

  Scenario: Reemplazar la imagen borra la anterior
    Given el producto 1 ya tiene una imagen
    When subo una imagen nueva
    Then la anterior ya no se sirve

  # Si la validación falla, el producto debe conservar la imagen que ya tenía.
  Scenario: Una subida inválida no destruye la imagen existente
    Given el producto 1 ya tiene una imagen
    When subo un archivo inválido
    Then recibo 400
    And la imagen anterior sigue sirviéndose

  Scenario: Borrar el producto borra su archivo
    Given el producto 1 tiene una imagen
    When borro el producto
    Then la ruta de la imagen deja de servirse

  Scenario: Subir imagen exige rol admin
    Given tengo un token con rol "user"
    When subo una imagen
    Then recibo 403
