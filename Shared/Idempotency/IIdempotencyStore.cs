namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Puerta de concurrencia para peticiones idénticas en vuelo. <b>No es la garantía de
/// idempotencia</b>: es un atajo delante de ella.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esto era antes el mecanismo entero</b>, y ahí estaba el error de diseño. Un almacén
/// que vive <i>fuera</i> de la transacción de negocio no puede garantizar nada: deja una
/// ventana entre el commit y el guardado de la marca (la compra ocurrió y nadie la
/// recuerda), y otra cuando no responde justo con carga, que es cuando el cliente
/// reintenta. Medido: 174 de 14 400 peticiones se ejecutaron sin garantía con Redis
/// <b>sano</b>. La garantía vive ahora en <see cref="ICommandLog"/>, o sea en la misma
/// transacción que el efecto.
/// </para>
/// <para>
/// Lo que queda aquí sí merece Redis: <b>frenar una tormenta de duplicados antes de que
/// se apile sobre la misma fila</b>. Sin esta puerta, 350 reintentos simultáneos con la
/// misma clave se quedarían todos bloqueados en la clave primaria, cada uno reteniendo su
/// conexión a la base. Con ella, el primero pasa y el resto recibe 409 sin tocar SQL.
/// </para>
/// <para>
/// Y como ya no es la garantía, degradar en abierto aquí dejó de significar «se puede
/// ejecutar dos veces» y pasa a significar «no hubo atajo». Por eso el marcador solo vive
/// <b>mientras la petición está en vuelo</b>: no hay respuestas memorizadas, ni ventana de
/// 24 h, ni una segunda copia del cuerpo que pueda diferir de la de verdad.
/// </para>
/// </remarks>
public interface IIdempotencyStore
{
  /// <summary>
  /// Marca la clave como «en vuelo» si nadie la tiene. Una sola operación atómica.
  /// </summary>
  /// <param name="key">Clave completa (usuario + método + ruta + clave del cliente).</param>
  /// <param name="ttl">
  /// Vida del marcador. Es solo una red por si el proceso muere a mitad: en el camino
  /// normal se libera al terminar la petición.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  Task<IdempotencyGate> TryEnterAsync(string key, TimeSpan ttl, CancellationToken ct = default);

  /// <summary>
  /// Suelta el marcador, si sigue siendo nuestro.
  /// </summary>
  /// <param name="key">Clave completa.</param>
  /// <param name="fence">
  /// Token de propiedad devuelto por <see cref="TryEnterAsync"/>. <b>Sin él no se borra
  /// nada.</b> Un borrado incondicional deja que una petición cuyo marcador ya caducó
  /// tire el marcador vivo de otra.
  /// </param>
  /// <param name="ct">Token de cancelación.</param>
  Task ReleaseAsync(string key, string fence, CancellationToken ct = default);
}


/// <summary>Cómo terminó el intento de cruzar la puerta.</summary>
public enum IdempotencyGateOutcome
{
  /// <summary>Nadie más está ejecutando esta clave: adelante.</summary>
  Entered,

  /// <summary>Hay una petición idéntica en vuelo ahora mismo.</summary>
  Busy,

  /// <summary>
  /// El almacén no contestó. Se sigue adelante: <b>la garantía no depende de esto</b>.
  /// </summary>
  /// <remarks>
  /// Se distingue de <see cref="Entered"/> para poder contarlo. No cambia el resultado
  /// —las dos ejecutan— pero una pasó por la puerta y la otra se la encontró rota, y eso
  /// es lo que hay que ver en una métrica antes de que se convierta en carga sobre SQL.
  /// </remarks>
  Unavailable
}


/// <summary>Resultado de <see cref="IIdempotencyStore.TryEnterAsync"/>.</summary>
/// <param name="Outcome">Cómo terminó el intento.</param>
/// <param name="Fence">
/// Token de propiedad del marcador que se acaba de poner, para devolvérselo a
/// <see cref="IIdempotencyStore.ReleaseAsync"/>. Vacío si no se puso ninguno.
/// </param>
public readonly record struct IdempotencyGate(IdempotencyGateOutcome Outcome, string Fence)
{
  public static IdempotencyGate Entered(string fence) => new(IdempotencyGateOutcome.Entered, fence);

  public static IdempotencyGate Busy { get; } = new(IdempotencyGateOutcome.Busy, string.Empty);

  public static IdempotencyGate Unavailable { get; } = new(IdempotencyGateOutcome.Unavailable, string.Empty);
}
