using Microsoft.AspNetCore.Identity;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Accounts.Models;


/// <summary>
/// Usuario de la aplicación. Hereda de <see cref="IdentityUser"/>, que aporta hash de
/// contraseña, normalización de email/username, bloqueo por intentos fallidos y roles.
/// </summary>
/// <remarks>
/// No implementa <see cref="IEntity"/> a propósito: su clave es un <c>string</c> y su ciclo de
/// vida lo gobierna <c>UserManager</c>, no el CRUD genérico.
/// </remarks>
public class ApplicationUser : IdentityUser
{
  /// <summary>Nombre para mostrar. El <c>UserName</c> heredado es el identificador de login.</summary>
  public string? Name { get; set; }

  /// <summary>Alta del usuario. Se estampa en el registro; Identity no trae auditoría.</summary>
  public DateTime CreatedAt { get; set; } = DateTime.Now;
}
