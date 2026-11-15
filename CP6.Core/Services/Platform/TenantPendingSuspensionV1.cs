using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Platform;

internal sealed class TenantPendingSuspensionV1
{
    public bool Legacy { get; set; }
    public byte[] OperationHash { get; set; } = [];
    public byte[] Operation { get; set; } = [];
    public byte[] Input { get; set; } = [];
    public Guid Actor { get; set; }
    public Guid ActorTenant { get; set; }
    public long Revision { get; set; }
    public Guid Store { get; set; }
    public int SourceVersion { get; set; }

    internal static Task<bool> ExistsAsync(CP6Context db, Guid tenant, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_tenant_management_v1.pending_suspensions WHERE tenant_id={tenant}) AS "Value"
            """).SingleAsync(ct);

    internal static Task<TenantPendingSuspensionV1?> ReadAsync(CP6Context db, Guid tenant, CancellationToken ct)
        => db.Database.SqlQuery<TenantPendingSuspensionV1>($"""
            SELECT is_legacy AS "Legacy",operation_hash AS "OperationHash",operation_bytes AS "Operation",input_bytes AS "Input",
              actor_id AS "Actor",actor_tenant AS "ActorTenant",expected_revision AS "Revision",store_id AS "Store",source_version AS "SourceVersion"
            FROM cp6_tenant_management_v1.pending_suspensions WHERE tenant_id={tenant}
            """).SingleOrDefaultAsync(ct);

    internal Task<int> InsertAsync(CP6Context db, Guid tenant, CancellationToken ct)
        => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_tenant_management_v1.pending_suspensions(
              tenant_id,is_legacy,operation_hash,operation_bytes,input_bytes,actor_id,actor_tenant,expected_revision,store_id,source_version)
            VALUES({tenant},{Legacy},{OperationHash},{Operation},{Input},{Actor},{ActorTenant},{Revision},{Store},{SourceVersion})
            """, ct);

    internal static async Task ClearAsync(CP6Context db, Guid tenant, CancellationToken ct)
    {
        if (await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM cp6_tenant_management_v1.pending_suspensions WHERE tenant_id={tenant}", ct) != 1)
            throw new TenantManagementExceptionV1("TENANT_OUTCOME_UNKNOWN", 503);
    }
}
