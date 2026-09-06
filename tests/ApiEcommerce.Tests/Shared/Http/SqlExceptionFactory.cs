using System.Reflection;
using Microsoft.Data.SqlClient;

namespace ApiEcommerce.Tests.Shared.Http;


/// <summary>
/// Fabrica un <see cref="SqlException"/> con un número de error concreto.
/// </summary>
/// <remarks>
/// <para>
/// <c>SqlException</c> no tiene constructor público —solo la crea el driver— así que
/// hay que llegar por reflexión al <c>CreateException</c> interno. Es feo y es
/// <b>frágil ante un cambio de versión de Microsoft.Data.SqlClient</b>, pero la
/// alternativa es no poder probar el mapeo 2601/2627/1205/547 sin levantar SQL Server,
/// y ese mapeo ya se rompió una vez en este proyecto.
/// </para>
/// <para>
/// Si algún día esto falla al actualizar el paquete, el fallo es <b>del helper</b>, no
/// del handler: se arregla aquí y los tests siguen valiendo.
/// </para>
/// </remarks>
internal static class SqlExceptionFactory
{
  public static SqlException WithNumber(int number)
  {
    var error = (SqlError)Activator.CreateInstance(
        typeof(SqlError),
        BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null,
        args: [number, (byte)0, (byte)0, "server", "mensaje de SQL Server", "procedimiento", 0, (Exception?)null],
        culture: null)!;

    var collection = (SqlErrorCollection)Activator.CreateInstance(
        typeof(SqlErrorCollection), BindingFlags.Instance | BindingFlags.NonPublic,
        binder: null, args: null, culture: null)!;

    typeof(SqlErrorCollection)
        .GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(collection, [error]);

    return (SqlException)typeof(SqlException)
        .GetMethod("CreateException", BindingFlags.Static | BindingFlags.NonPublic,
                   binder: null, types: [typeof(SqlErrorCollection), typeof(string)], modifiers: null)!
        .Invoke(null, [collection, "16.0.0"])!;
  }
}
