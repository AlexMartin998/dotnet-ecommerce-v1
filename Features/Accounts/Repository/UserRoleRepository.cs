using System.Data;
using ApiEcommerce.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ApiEcommerce.Features.Accounts.Repository;


/// <inheritdoc cref="IUserRoleRepository"/>
public sealed class UserRoleRepository(AppDbContext db) : IUserRoleRepository
{
  /// <summary>Recurso bloqueado, global a la base como el del publicador del outbox.</summary>
  public const string AdminRoleLockName = "apiecommerce:admin-role";

  public Task<int> CountUsersInRoleAsync(string role, CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(role);

    // Identity guarda el nombre normalizado en mayúsculas; el rol llega tal cual lo escribe el slice.
    var normalized = role.ToUpperInvariant();

    return db.UserRoles
        .AsNoTracking()
        .CountAsync(ur => db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalized), ct);
  }

  public async Task<bool> TryLockAdminRoleAsync(TimeSpan timeout, CancellationToken ct = default)
  {
    var transaction = db.Database.CurrentTransaction
        ?? throw new InvalidOperationException("The admin role lock requires an active transaction.");

    await using var command = db.Database.GetDbConnection().CreateCommand();

    command.Transaction = transaction.GetDbTransaction();
    command.CommandType = CommandType.StoredProcedure;
    command.CommandText = "sp_getapplock";

    command.Parameters.Add(new SqlParameter("@Resource", AdminRoleLockName));
    command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
    // 'Transaction': se suelta en el commit, aunque el proceso muera a mitad.
    command.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
    // Con espera y no 0 como el outbox: aquí la operación es del usuario y saltársela sería perderla.
    command.Parameters.Add(new SqlParameter("@LockTimeout", (int)timeout.TotalMilliseconds));

    var result = new SqlParameter
    {
      ParameterName = "@Result",
      SqlDbType = SqlDbType.Int,
      Direction = ParameterDirection.ReturnValue
    };

    command.Parameters.Add(result);

    await command.ExecuteNonQueryAsync(ct);

    // >= 0 concedido (0 inmediato, 1 tras esperar); < 0 no concedido.
    return result.Value is int code && code >= 0;
  }
}
