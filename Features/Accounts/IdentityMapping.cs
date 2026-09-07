using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Models;
using Microsoft.AspNetCore.Identity;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Traducción entre lo que devuelve ASP.NET Identity y lo que sale del slice: el DTO de
/// usuario y la forma de los errores de validación.
/// </summary>
/// <remarks>
/// En un solo sitio porque estaba duplicada en <c>AuthService</c> y <c>UserAdminService</c>,
/// y las dos copias habían divergido: una agrupaba los errores por campo y la otra los
/// metía todos bajo la clave <c>identity</c>. Para el cliente eran dos 422 distintos.
/// </remarks>
internal static class IdentityMapping
{
  /// <summary>
  /// Proyección a mano y no AutoMapper: los roles son una consulta aparte, y un Profile
  /// tendría que inyectar el <c>UserManager</c> para resolverlos.
  /// </summary>
  public static UserDto ToDto(ApplicationUser user, IEnumerable<string> roles) => new()
  {
    Id = user.Id,
    Username = user.UserName ?? string.Empty,
    Email = user.Email,
    Name = user.Name,
    Roles = [.. roles],
    CreatedAt = user.CreatedAt
  };

  /// <summary>El fallo de Identity, como excepción de dominio con los errores por campo.</summary>
  public static ValidationAppException Failure(IdentityResult result)
      => new(ToFieldErrors(result));

  /// <summary>
  /// Agrupa los <c>IdentityError</c> en el formato <c>{ campo: [mensajes] }</c> de
  /// <c>ValidationProblem(ModelState)</c>, para que el cliente vea siempre la misma forma.
  /// </summary>
  private static Dictionary<string, string[]> ToFieldErrors(IdentityResult result)
      => result.Errors
          .GroupBy(e => FieldFor(e.Code))
          .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

  /// <summary>A qué campo del request culpa cada código de Identity.</summary>
  /// <remarks>Lo que no se puede atribuir cae en <c>request</c>, no en una clave inventada.</remarks>
  private static string FieldFor(string identityErrorCode) => identityErrorCode switch
  {
    var c when c.Contains("Password", StringComparison.Ordinal) => nameof(RegisterUserDto.Password),
    var c when c.Contains("Email", StringComparison.Ordinal) => nameof(RegisterUserDto.Email),
    var c when c.Contains("UserName", StringComparison.Ordinal) => nameof(RegisterUserDto.Username),
    _ => "request"
  };
}
