using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Body de <c>POST /api/v1/auth/register</c>.</summary>
public class RegisterUserDto
{
  [Required(ErrorMessage = "Username is required")]
  [MinLength(4, ErrorMessage = "Username can't be shorter than 4 characters")]
  [MaxLength(50, ErrorMessage = "Username can't be longer than 50 characters")]
  [RegularExpression(@"^[a-zA-Z0-9._-]+$", ErrorMessage = "Username can only contain letters, digits, dot, underscore and hyphen")]
  public string Username { get; set; } = string.Empty;

  [Required(ErrorMessage = "Email is required")]
  [EmailAddress(ErrorMessage = "Email must be a valid address")]
  [MaxLength(150, ErrorMessage = "Email can't be longer than 150 characters")]
  public string Email { get; set; } = string.Empty;

  [MaxLength(100, ErrorMessage = "Name can't be longer than 100 characters")]
  public string? Name { get; set; }

  // La política de fuerza real la aplica Identity; aquí solo se corta lo obviamente inválido.
  [Required(ErrorMessage = "Password is required")]
  [MinLength(8, ErrorMessage = "Password can't be shorter than 8 characters")]
  [MaxLength(100, ErrorMessage = "Password can't be longer than 100 characters")]
  public string Password { get; set; } = string.Empty;
}
