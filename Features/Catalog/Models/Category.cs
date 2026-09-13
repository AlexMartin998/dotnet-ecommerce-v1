using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Models;


/// <summary>Categoría del catálogo.</summary>
/// <remarks>
/// El índice único es la única garantía real: la comprobación de <c>CategoryRules</c> da
/// un 409 con mensaje útil, pero entre ella y el INSERT cabe otra petición.
/// </remarks>
[Index(nameof(Name), IsUnique = true)]
[Index(nameof(Slug), IsUnique = true)]
public class Category : IAuditable
{

  [Key]
  public int Id { get; set; }

  // MaxLength no es cosmético: sin él la columna es nvarchar(max) y SQL Server no puede
  // indexarla.
  [Required]
  [MaxLength(50)]
  public required string Name { get; set; }

  /// <summary>Identificador de la URL pública. Sale del nombre al crearla y no cambia.</summary>
  [Required]
  [MaxLength(60)]
  public string Slug { get; set; } = string.Empty;

  [MaxLength(200)]
  public string? Description { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  [Required]
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;

}
