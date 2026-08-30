Feature: Autorización por roles
  Como dueño del sistema
  quiero que cada endpoint exija exactamente el permiso que le corresponde
  para que el catálogo sea público pero nadie más lo modifique

  # Los controllers llevan [Authorize] a nivel de CLASE (el requisito más débil) porque
  # varios [Authorize] se COMBINAN (AND) y solo [AllowAnonymous] gana sobre la clase.
  # Con [Authorize(Roles="admin")] en la clase, la compra daba 403 a un usuario normal.

  Scenario Outline: El catálogo es público
    When pido <endpoint> sin token
    Then recibo 200

    Examples:
      | endpoint                              |
      | GET /api/v1/category                  |
      | GET /api/v1/category/paged            |
      | GET /api/v1/product                   |
      | GET /api/v1/product/paged             |
      | GET /api/v1/product/search?name=x     |
      | GET /api/v1/product/category/1        |

  Scenario Outline: Las escrituras exigen rol admin
    Given tengo un token de un usuario con rol "user"
    When llamo a <endpoint>
    Then recibo 403

    Examples:
      | endpoint                           |
      | POST /api/v1/category              |
      | PATCH /api/v1/category/1           |
      | DELETE /api/v1/category/1          |
      | POST /api/v1/product               |
      | PATCH /api/v1/product/1            |
      | DELETE /api/v1/product/1           |
      | POST /api/v1/product/1/image       |

  # Exigir admin para comprar —como hacía el código de referencia— no tiene sentido
  # en una tienda. La compra hereda el [Authorize] de la clase: basta estar autenticado.
  Scenario: Comprar solo exige estar autenticado
    Given tengo un token de un usuario con rol "user"
    When llamo a "POST /api/v1/product/buy" con un SKU con stock
    Then recibo 200

  Scenario: Comprar sin token no se permite
    When llamo a "POST /api/v1/product/buy" sin token
    Then recibo 401

  Scenario: Un endpoint nuevo sin atributo queda protegido, no abierto
    Given una acción nueva en un controller sin atributo de autorización
    When la llamo sin token
    Then recibo 401
