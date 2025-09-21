using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Models;


public class Category
{

  [Key]
  public int Id { get; set; }

  [Required]
  public required string Name { get; set; }

  public string? Description { get; set; }

  [Required]
  public DateTime CreatedAt { get; set; } = DateTime.Now;

}