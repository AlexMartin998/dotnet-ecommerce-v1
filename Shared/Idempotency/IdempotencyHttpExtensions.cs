using ApiEcommerce.Shared.Auth;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Traduce la cabecera <c>Idempotency-Key</c> a una <see cref="CommandIntent"/>.
/// </summary>
/// <remarks>
/// Es la costura entre protocolo y dominio, por eso vive en un helper de HTTP y lo usa el
/// controller: el servicio habla de intenciones, no de cabeceras. Que <c>[Idempotent]</c>
/// lea la misma cabecera no es duplicación, son dos niveles con el mismo dato de entrada.
/// </remarks>
public static class IdempotencyHttpExtensions
{
  /// <summary>
  /// Intención declarada por el cliente para esta petición, o
  /// <see cref="CommandIntent.None"/> si no declaró ninguna.
  /// </summary>
  /// <remarks>
  /// La clave se acota al usuario: sin eso, dos clientes que generaran el mismo GUID se
  /// pisarían y uno recibiría la respuesta del otro. Sin usuario no hay intención.
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
