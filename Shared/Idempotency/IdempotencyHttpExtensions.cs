using ApiEcommerce.Shared.Auth;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Traduce la cabecera <c>Idempotency-Key</c> a una <see cref="CommandIntent"/>.
/// </summary>
/// <remarks>
/// <para>
/// Esta es la costura entre el protocolo y el dominio, y por eso vive en un helper de
/// HTTP y no en el servicio: el servicio habla de <b>intenciones</b>, no de cabeceras.
/// Un job que reprocesa una cola construye la suya con el <c>MessageId</c> y no pasa por
/// aquí.
/// </para>
/// <para>
/// Lo usa el <b>controller</b>, que es un adaptador de entrada y sí puede leer
/// <c>HttpContext</c>. El <c>[Idempotent]</c> lee la misma cabecera para su atajo en
/// Redis; que ambos la lean no es duplicación, son dos niveles distintos con el mismo
/// dato de entrada.
/// </para>
/// </remarks>
public static class IdempotencyHttpExtensions
{
  /// <summary>
  /// Intención declarada por el cliente para esta petición, o
  /// <see cref="CommandIntent.None"/> si no declaró ninguna.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Sin cabecera no hay intención: la idempotencia la pide el cliente, que es quien sabe
  /// si está reintentando. Y esa ausencia es además <b>la vía de escape estándar</b>: es
  /// lo que documenta Adyen para cuando un cliente prefiere seguir sin garantía. La
  /// decisión de renunciar es suya y explícita, no una degradación silenciosa nuestra.
  /// </para>
  /// <para>
  /// ⚠️ La clave se acota al <b>usuario</b>. Sin eso, dos clientes que casualmente
  /// generen el mismo GUID se pisarían — y peor, uno recibiría la respuesta del otro,
  /// que es una fuga de datos entre cuentas. Sin usuario identificado no hay intención.
  /// </para>
  /// </remarks>
  /// <param name="context">La petición en curso.</param>
  /// <param name="operation">Nombre de la operación, en el lenguaje del dominio.</param>
  public static CommandIntent CommandIntentFor(this HttpContext context, string operation)
  {
    ArgumentNullException.ThrowIfNull(context);

    if (!context.Request.Headers.TryGetValue(IdempotentAttribute.HeaderName, out var header))
      return CommandIntent.None;

    var key = header.ToString();

    if (string.IsNullOrWhiteSpace(key)) return CommandIntent.None;

    var user = context.User.GetUserId();

    return string.IsNullOrEmpty(user)
        ? CommandIntent.None
        : new CommandIntent(operation, $"{user}:{key}");
  }
}
