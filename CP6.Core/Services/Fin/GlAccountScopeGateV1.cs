using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Fin.GlAccountWorkflowStoreV1;

namespace CP6.Core.Services.Fin;

/// <summary>取得当前身份及专业来源门后，参与同一 PostgreSQL 事务的账套门。</summary>
internal static class GlAccountScopeGateV1
{
    internal static async Task Enter(CP6Context db, Guid ledger, bool exclusive, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction is null || db.CurrentTenantId == Guid.Empty)
            throw Error("OWNER_PARTICIPANT_UNPROVEN", 503);
        var key = $"cp6-fin-gl/v1/{db.CurrentTenantId:N}/{ledger:N}";
        var entered = exclusive
            ? await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock(hashtextextended({key},0)) AS \"Value\"").SingleAsync(ct)
            : await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_xact_lock_shared(hashtextextended({key},0)) AS \"Value\"").SingleAsync(ct);
        if (!entered) throw Error("TECHNICAL_RETRY", 503);
    }
}
