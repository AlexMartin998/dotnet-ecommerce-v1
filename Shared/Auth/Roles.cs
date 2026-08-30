namespace ApiEcommerce.Shared.Auth;


/// <summary>
/// Nombres de rol como constantes. Equivale a las constantes que en Spring Security
/// se usan con <c>@PreAuthorize("hasRole('ADMIN')")</c>.
/// </summary>
/// <remarks>
/// Son <c>const</c> y no <c>static readonly</c> a propósito: <c>[Authorize(Roles = ...)]</c>
/// es un atributo y solo admite constantes de compilación. Un string mágico repetido
/// en 12 endpoints es un typo esperando a pasar en producción.
/// </remarks>
public static class Roles
{
  public const string Admin = "admin";
  public const string User = "user";

  /// <summary>Todos los roles que siembra <c>DataSeeder</c>.</summary>
  public static readonly string[] All = [Admin, User];
}
