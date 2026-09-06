using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Dtos;

namespace ApiEcommerce.Features.Catalog.Models;


/// <summary>Producto del catálogo.</summary>
[Index(nameof(SKU), IsUnique = true)]
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

  [MaxLength(300)]
  public string? ImageUrl { get; set; }

  [Required]
  [MaxLength(50)]
  public required string SKU { get; set; } // Stock Keeping Unit - PROD-001-BLK-M

  [Range(0, int.MaxValue)]
  public int Stock { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;


  /// <summary>
  /// Token de concurrencia optimista que SQL Server mantiene solo. EF lo añade al
  /// <c>WHERE</c> de todo UPDATE y lanza <c>DbUpdateConcurrencyException</c> si cambió.
  /// </summary>
  /// <remarks>
  /// Por sí sola no cierra el <i>lost update</i> entre dos PATCH, que necesita el
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
