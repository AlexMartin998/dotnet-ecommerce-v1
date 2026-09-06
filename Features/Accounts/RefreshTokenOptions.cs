using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Accounts;


/// <summary>Sesiones y rotación de refresh tokens, sección <c>RefreshToken</c>.</summary>
public sealed class RefreshTokenOptions
{
  public const string SectionName = "RefreshToken";

  /// <summary>Cuánto puede durar una sesión sin volver a poner la contraseña.</summary>
  [Range(1, 365)]
  public int LifetimeDays { get; init; } = 14;

  /// <summary>Nombre de la cookie que transporta el refresh token.</summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>NO se usa el prefijo <c>__Host-</c></b>, y conviene saber por qué, porque es
  /// tentador: ese prefijo obliga al navegador a exigir <c>Secure</c> <b>y</b>
  /// <c>Path=/</c> <b>y</b> ningún <c>Domain</c>, y si algo no cuadra <b>descarta la
  /// cookie sin decir nada</b>. Aquí choca dos veces: la cookie va con <c>Path</c> acotado
  /// a <c>/api/v1/auth</c> —no hay razón para mandarla en cada petición al catálogo— y en
  /// Development se sirve por HTTP, donde <c>Secure</c> no vale.
  /// </para>
  /// <para>
  /// El fallo habría sido de los peores: el login responde 200, la cookie no se guarda, y
  /// el refresh falla siempre <b>sin un solo error en el servidor</b>. Lo que aporta el
  /// prefijo —que un subdominio no pueda sobrescribirla— se cubre aquí con
  /// <c>SameSite=Strict</c> y con que la API no comparta dominio con nada.
  /// </para>
  /// </remarks>
  [Required]
  [MaxLength(64)]
  public string CookieName { get; init; } = "rt";

  /// <summary>
  /// Ventana en la que reusar un token recién gastado se toma por una carrera del cliente
  /// y <b>no</b> por un robo.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ Sin esto, la detección de reuso es inutilizable en la práctica. Un móvil o una SPA
  /// lanzan varias peticiones a la vez; si dos reciben 401 casi al mismo tiempo, las dos
  /// refrescan con el mismo token y la segunda parece un ladrón. El resultado sería cerrar
  /// la sesión de usuarios legítimos constantemente.
  /// </para>
  /// <para>
  /// Dentro de la ventana se rechaza igual (401: ese token ya está gastado) pero <b>no</b>
  /// se revoca la familia, así que el token nuevo que ya recibió la otra petición sigue
  /// valiendo. Fuera de la ventana sí es señal de robo.
  /// </para>
  /// <para>
  /// Es la misma idea que el <i>leeway</i> de Auth0. El precio es que un ladrón que
  /// reutilice el token en los primeros segundos pasa desapercibido — a cambio de que el
  /// mecanismo se pueda tener encendido, que es lo que de verdad protege.
  /// </para>
  /// </remarks>
  [Range(0, 300)]
  public int ReuseGraceSeconds { get; init; } = 15;

  public TimeSpan Lifetime => TimeSpan.FromDays(LifetimeDays);

  public TimeSpan ReuseGrace => TimeSpan.FromSeconds(ReuseGraceSeconds);
}
