using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cmr.Api.Data;

public sealed class RlsConnectionInterceptor(CurrentUser current) : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection, current.TenantId);

    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default) =>
        await ApplyAsync(connection, current.TenantId, cancellationToken);

    public static void Apply(DbConnection connection, Guid? tenantId)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.tenant_id', @id, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = tenantId?.ToString() ?? "";
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    public static async Task ApplyAsync(DbConnection connection, Guid? tenantId, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT set_config('app.tenant_id', @id, false)";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = tenantId?.ToString() ?? "";
        cmd.Parameters.Add(p);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
