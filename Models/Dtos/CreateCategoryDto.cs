using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Models.Dtos;


public class CreateCategoryDto
{

  [Required(ErrorMessage = "Name is required")]
  [MaxLength(50, ErrorMessage = "Name can't be longer than 50 characters")]
  [MinLength(3, ErrorMessage = "Name can't be shorter than 3 characters")]
  [RegularExpression(@"^[a-zA-Z0-9\s]+$", ErrorMessage = "Name can't have special characters")]
  public string Name { get; set; } = string.Empty;

  [MaxLength(200, ErrorMessage = "Description can't be longer than 200 characters")]
  public string? Description { get; set; }

}
