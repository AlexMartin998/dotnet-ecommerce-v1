using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Models;


// El índice único en BD es la ÚNICA garantía real de unicidad. CategoryRules
// comprueba el nombre antes de escribir, pero entre esa comprobación y el INSERT
// cabe otra petición: dos POST simultáneos con el mismo nombre pasaban los dos.
// La regla sigue existiendo porque da un 409 con mensaje útil en el caso normal;
// el índice es la red que atrapa la carrera.
[Index(nameof(Name), IsUnique = true)]
public class Category : IAuditable
{

  [Key]
  public int Id { get; set; }

  // MaxLength no es cosmético: sin él la columna es nvarchar(max) y SQL Server
  // NO puede indexarla (el límite de clave son 900 bytes). Además hace que la BD
  // imponga lo mismo que ya promete el DTO.
  [Required]
  [MaxLength(50)]
  public required string Name { get; set; }

  [MaxLength(200)]
  public string? Description { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  [Required]
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;

}
