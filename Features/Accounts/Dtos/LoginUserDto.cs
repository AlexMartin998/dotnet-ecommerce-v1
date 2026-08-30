using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts.Dtos;


/// <summary>Body de <c>POST /api/v1/auth/login</c>.</summary>
public class LoginUserDto
{
  [Required(ErrorMessage = "Username is required")]
  [MaxLength(50, ErrorMessage = "Username can't be longer than 50 characters")]
  public string Username { get; set; } = string.Empty;

  [Required(ErrorMessage = "Password is required")]
  [MaxLength(100, ErrorMessage = "Password can't be longer than 100 characters")]
  public string Password { get; set; } = string.Empty;
}
