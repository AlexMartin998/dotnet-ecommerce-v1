using Microsoft.AspNetCore.Identity;

namespace ApiEcommerce.Models;


/// <summary>
/// Usuario de la aplicación. Hereda de <see cref="IdentityUser"/>, así que ASP.NET
/// Core Identity aporta gratis: hash de contraseña (PBKDF2 + salt), normalización de
/// email/username, bloqueo por intentos fallidos, tokens de confirmación y roles.
/// </summary>
/// <remarks>
/// <para>
/// <b>No implementa <see cref="IEntity"/> a propósito.</b> Su clave es un
/// <c>string</c> (GUID), no un <c>int</c>, así que no encaja en
/// <c>BaseRepository&lt;T&gt;</c> ni en <c>CrudService&lt;...&gt;</c> — y no debe
/// encajar: el ciclo de vida de un usuario (registro, login, roles, bloqueo) lo
/// gobierna <c>UserManager</c>, no un CRUD genérico. Forzarlo dentro del genérico
/// sería el clásico error de meter una entidad en una abstracción que no le sirve.
/// </para>
/// <para>Equivale a la entidad <c>User</c> + <c>UserDetailsService</c> de Spring Security.</para>
/// </remarks>
public class ApplicationUser : IdentityUser
{
  /// <summary>Nombre para mostrar. El <c>UserName</c> heredado es el identificador de login.</summary>
  public string? Name { get; set; }

  /// <summary>Alta del usuario. Se estampa en el registro; Identity no trae auditoría.</summary>
  public DateTime CreatedAt { get; set; } = DateTime.Now;
}
