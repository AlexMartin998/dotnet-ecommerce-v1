using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Models;


public class Category : IAuditable
{

  [Key]
  public int Id { get; set; }

  [Required]
  public required string Name { get; set; }

  public string? Description { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  [Required]
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;

}
