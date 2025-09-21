using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ApiEcommerce.Models;


public class Product
{

  [Key]
  public int Id { get; set; }

  [Required]
  public required string Name { get; set; }

  public string? Description { get; set; }

  [Range(0, double.MaxValue)] // Price must be non-negative
  [Column(TypeName = "decimal(18,2)")] // Precision and scale for SQL Server
  public decimal Price { get; set; }

  public string? ImageUrl { get; set; }

  [Required]
  public required string SKU { get; set; } // Stock Keeping Unit - PROD-001-BLK-M

  [Range(0, int.MaxValue)] // Stock must be non-negative
  public int Stock { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;


  // Foreign Key --------
  public int CategoryId { get; set; }

  [ForeignKey("CategoryId")]
  public required Category Category { get; set; }
  // https://learn.microsoft.com/es-mx/ef/core/modeling/relationships

}
