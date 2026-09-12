using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Catalog.Models;


/// <summary>Producto del catálogo.</summary>
/// <remarks>
/// Los dos índices únicos van <b>filtrados por <c>DeletedAt</c></b>: con el borrado lógico,
/// un producto retirado seguiría bloqueando su propio SKU y su slug para siempre.
/// </remarks>
[Index(nameof(SKU), IsUnique = true)]
[Index(nameof(Slug), IsUnique = true)]
public class Product : IAuditable
{

  [Key]
  public int Id { get; set; }

  // Longitudes alineadas con las DataAnnotations de los DTOs.
  [Required]
  [MaxLength(100)]
  public required string Name { get; set; }

  [MaxLength(500)]
  public string? Description { get; set; }

  [Range(0, double.MaxValue)]
  [Column(TypeName = "decimal(18,2)")]
  public decimal Price { get; set; }


  [Required]
  [MaxLength(50)]
  public required string SKU { get; set; } // Stock Keeping Unit - PROD-001-BLK-M

  [Range(0, int.MaxValue)]
  public int Stock { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;


  /// <summary>Identificador para la URL pública, único entre los productos vivos.</summary>
  /// <remarks>
  /// El front lo usa en <c>/product/{slug}</c>, que es lo que permite generar la página
  /// estáticamente. Se deriva del nombre si no se manda, pero no se recalcula al renombrar:
  /// cambiarlo rompería los enlaces que ya circulan.
  /// </remarks>
  // Sin `required`, al contrario que Name y SKU: no lo manda el cliente, lo DERIVA
  // ProductMapper.ToEntity, y un generador no puede satisfacer un miembro requerido con un
  // valor calculado. La base lo sigue exigiendo (NOT NULL) y el índice único lo vigila.
  [Required]
  [MaxLength(200)]
  public string Slug { get; set; } = string.Empty;

  /// <summary>Etiquetas de navegación y búsqueda: <c>men</c>, <c>shirts</c>, <c>oferta</c>…</summary>
  /// <remarks>
  /// Colección primitiva: EF la guarda como JSON en una columna. No hay tabla aparte porque
  /// no tienen atributos propios ni se consultan por sí solas, solo por producto.
  /// </remarks>
  public List<string> Tags { get; set; } = [];

  /// <summary>Tallas o presentaciones disponibles. Informativas.</summary>
  /// <remarks>
  /// El stock sigue siendo <b>por producto</b>, no por talla: stock por variante cambia el
  /// contrato de la compra y toda la reserva, y eso no se improvisa aquí.
  /// </remarks>
  public List<string> Sizes { get; set; } = [];

  /// <summary>Cuándo se retiró del catálogo. <c>null</c> mientras está a la venta.</summary>
  /// <remarks>
  /// Borrado lógico y no físico porque las órdenes apuntan al producto por id: borrarlo de
  /// verdad dejaría esa referencia colgando. Un filtro global lo esconde de toda consulta.
  /// </remarks>
  public DateTime? DeletedAt { get; set; }

  /// <summary>Imágenes públicas, en el orden en que se enseñan.</summary>
  public ICollection<ProductImage> Images { get; set; } = [];

  /// <summary>
  /// Token de concurrencia optimista que SQL Server mantiene solo. EF lo añade al <c>WHERE</c>
  /// de los UPDATE que salen del change tracker, no a los de <c>ExecuteUpdateAsync</c>.
  /// </summary>
  /// <remarks>
  /// Por sí sola no cierra el lost update entre dos PATCH, que necesita el
  /// <c>ETag</c>/<c>If-Match</c> de <c>ProductRules</c>: aquí es la segunda red, para la
  /// ventana entre esa comparación y el UPDATE.
  /// </remarks>
  [Timestamp]
  public byte[]? RowVersion { get; set; }


  // Foreign Key --------
  public int CategoryId { get; set; }

  // La navegación es opcional en C# a propósito: la relación sigue siendo obligatoria en
  // la base (CategoryId no es nullable), pero `required Category` impedía que AutoMapper
  // construyera un Product desde CreateProductDto.
  [ForeignKey(nameof(CategoryId))]
  public Category? Category { get; set; }

}
