namespace ApiEcommerce.Shared.Auth;


/// <summary>Nombres de rol como constantes, para no repetir strings mágicos.</summary>
/// <remarks>
/// Son <c>const</c> y no <c>static readonly</c> porque <c>[Authorize(Roles = ...)]</c> es
/// un atributo y solo admite constantes de compilación.
/// </remarks>
public static class Roles
{
  /// <summary>Rol administrador.</summary>
  public const string Admin = "admin";

  /// <summary>Rol de usuario registrado.</summary>
  public const string User = "user";

  /// <summary>Todos los roles que siembra <c>DataSeeder</c>.</summary>
  public static readonly string[] All = [Admin, User];
}
