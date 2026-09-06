using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Dtos;

namespace ApiEcommerce.Features.Catalog.Models;


[Index(nameof(SKU), IsUnique = true)] // Unique constraint on SKU
public class Product : IAuditable
{

  [Key]
  public int Id { get; set; }

  // Longitudes alineadas con las DataAnnotations de los DTOs: la base impone lo
  // mismo que promete el contrato de la API.
  [Required]
  [MaxLength(100)]
  public required string Name { get; set; }

  [MaxLength(500)]
  public string? Description { get; set; }

  [Range(0, double.MaxValue)] // Price must be non-negative
  [Column(TypeName = "decimal(18,2)")] // Precision and scale for SQL Server
  public decimal Price { get; set; }

  [MaxLength(300)]
  public string? ImageUrl { get; set; }

  [Required]
  [MaxLength(50)]
  public required string SKU { get; set; } // Stock Keeping Unit - PROD-001-BLK-M

  [Range(0, int.MaxValue)] // Stock must be non-negative
  public int Stock { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;


  /// <summary>
  /// Token de <b>concurrencia optimista</b>. SQL Server lo mantiene solo (columna
  /// <c>rowversion</c>): cambia en cada UPDATE de la fila.
  /// </summary>
  /// <remarks>
  /// <para>
  /// EF lo añade al <c>WHERE</c> de todo UPDATE. Si otra petición modificó la fila
  /// entremedias, el UPDATE afecta a 0 filas y EF lanza
  /// <c>DbUpdateConcurrencyException</c> en vez de pisar el cambio ajeno.
  /// </para>
  /// <para>
  /// <b>Lo que SÍ garantiza hoy:</b> que un PATCH de administrador que toque
  /// <c>Stock</c> choque (409) con una compra concurrente, porque
  /// <c>TryDecrementStockAsync</c> cambia el <c>rowversion</c> por fuera del change
  /// tracker.
  /// </para>
  /// <para>
  /// <b>El <i>lost update</i> entre dos administradores ya está cerrado</b> (2026-09-06),
  /// pero <b>no por esta columna sola</b>: por sí misma no puede. El PATCH relee la fila,
  /// así que EF compara contra el <c>rowversion</c> recién leído —el del otro— y todo
  /// cuadra. Lo que lo cierra es publicar el token como <c>ETag</c> en el GET y compararlo
  /// contra el <c>If-Match</c> que devuelve el cliente (<c>ProductRules</c>): ese es el
  /// único valor que prueba <i>qué versión leyó de verdad</i>. Esta columna sigue siendo
  /// necesaria como segunda red, para la ventana entre esa comparación y el UPDATE.
  /// </para>
  /// <para>
  /// <b>No es lo que impide sobrevender stock:</b> eso lo resuelve el UPDATE
  /// condicional atómico de <c>TryDecrementStockAsync</c>. La concurrencia optimista
  /// aplicada a un contador con mucha contención rechaza compras válidas al agotar
  /// los reintentos.
  /// </para>
  /// </remarks>
  [Timestamp]
  public byte[]? RowVersion { get; set; }


  // Foreign Key --------
  public int CategoryId { get; set; }

  // La navegación es OPCIONAL en C# (`Category?`) a propósito: la relación sigue
  // siendo obligatoria en la base porque `CategoryId` es `int` no-nullable, pero
  // dejarla como `required Category` impedía que AutoMapper construyera un Product
  // desde CreateProductDto (ver AGENTS/docs/05-convenciones.md → Mapping).
  [ForeignKey(nameof(CategoryId))]
  public Category? Category { get; set; }
  // https://learn.microsoft.com/es-mx/ef/core/modeling/relationships

}
