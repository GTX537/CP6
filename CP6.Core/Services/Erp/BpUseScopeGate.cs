using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Core.Services.Erp;

// 伙伴主写者与 I02 使用同一个数据库事务锁。未切换的租户仍走原部署协议。
internal static class BpUseScopeGate
{
    internal static OrderPriceCorrectionException Unproven() => new("OWNER_PARTICIPANT_UNPROVEN", 503);
    internal static OrderPriceCorrectionException LockUnproven() => new("LOCK_PLAN_UNPROVEN", 503);

    internal static async Task<bool> IsCutoverAsync(CP6Context db, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || !await db.Database.SqlQueryRaw<bool>(
            "SELECT to_regclass('main_bp.use_writer_scopes') IS NOT NULL AS \"Value\"").SingleAsync(ct)) return false;
        return await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM main_bp.use_writer_scopes WHERE tenant_id={db.CurrentTenantId}) AS "Value"
            """).SingleAsync(ct);
    }

    internal static void RequireTransaction(CP6Context db, DbTransaction? tx, bool serializable = false)
    {
        if (tx is not NpgsqlTransaction native || db.CurrentTenantId == Guid.Empty || !db.Database.IsNpgsql() ||
            System.Transactions.Transaction.Current is not null ||
            !ReferenceEquals(native, db.Database.CurrentTransaction?.GetDbTransaction()) ||
            !ReferenceEquals(native.Connection, db.Database.GetDbConnection()) ||
            (serializable ? native.IsolationLevel != IsolationLevel.Serializable :
                native.IsolationLevel is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable)))
            throw Unproven();
    }

    internal static async Task HoldIamAsync(CP6Context db, CancellationToken ct)
    {
        RequireTransaction(db, db.Database.CurrentTransaction?.GetDbTransaction());
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT singleton AS "Value" FROM cp6_platform_admin_guard_v1.root WHERE singleton FOR SHARE
            """).SingleOrDefaultAsync(ct)) throw LockUnproven();
    }

    internal static async Task LockAsync(CP6Context db, string legal, bool exclusive, CancellationToken ct)
    {
        RequireTransaction(db, db.Database.CurrentTransaction?.GetDbTransaction());
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT main_bp.lock_use_scope({db.CurrentTenantId},{legal},{exclusive}) AS "Value"
            """).SingleAsync(ct)) throw LockUnproven();
    }

    internal static async Task RequireWriterAsync(CP6Context db, string legal, CancellationToken ct)
    {
        await LockAsync(db, legal, true, ct);
        // 现有财务引用会取伙伴表的共享锁。主写者在 Scope X 后先确定表锁模式，避免写前升级。
        await db.Database.ExecuteSqlRawAsync("""LOCK TABLE public."T_WebBusinessPartner" IN SHARE ROW EXCLUSIVE MODE""", ct);
        await RequireGuardsAsync(db, ct);
        if (!await PrimaryWriterAsync(db, legal, ct))
            throw new OrderPriceCorrectionException("BP_SCOPE_READ_ONLY", 409);
    }

    internal static Task<bool> PrimaryWriterAsync(CP6Context db, string legal, CancellationToken ct)
        => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM main_bp.use_writer_scopes
              WHERE tenant_id={db.CurrentTenantId} AND legal_entity_key={legal} AND primary_writer='WORKSPACE') AS "Value"
            """).SingleAsync(ct);

    internal static async Task RequireGuardsAsync(CP6Context db, CancellationToken ct)
    {
        if (!await db.Database.SqlQueryRaw<bool>("""SELECT main_bp.use_guards_ready() AS "Value" """).SingleAsync(ct))
            throw LockUnproven();
    }

    internal static async Task RequireBeforeEstimateAndQuoteAsync(CP6Context db, CancellationToken ct)
    {
        if (await db.Database.SqlQueryRaw<bool>("""
            SELECT EXISTS (SELECT FROM pg_catalog.pg_locks
              WHERE pid=pg_backend_pid() AND locktype='relation' AND granted AND mode<>'AccessShareLock'
                AND relation IN ('public."T_EstimateCalc"'::regclass,'public."T_EstimateCalcProcess"'::regclass,
                                 'public."T_ProductMaster"'::regclass,'public."T_ProductProcess"'::regclass,
                                 'public."T_ProductMaterial"'::regclass,'public."T_Quotation"'::regclass,'public."T_QuotationDetail"'::regclass,
                                 'public."T_Order"'::regclass,'public."T_OrderDetail"'::regclass)) AS "Value"
            """).SingleAsync(ct)) throw LockUnproven();
    }

    internal static async Task LockLegacyWriterAsync(CP6Context db, Guid? bp, string? baseCd, CancellationToken ct)
    {
        var legal = bp is { } id
            ? await db.Database.SqlQuery<string>($"""
                SELECT legal_entity_key AS "Value" FROM main_bp.heads WHERE tenant_id={db.CurrentTenantId} AND bp_id={id}
                """).SingleOrDefaultAsync(ct)
            : null;
        if (legal is null && !string.IsNullOrWhiteSpace(baseCd))
            legal = await db.Database.SqlQuery<string>($"""
                SELECT legal_entity_key AS "Value" FROM main_bp.bases
                 WHERE tenant_id={db.CurrentTenantId} AND base_cd={baseCd.TrimEnd()} AND enabled
                """).SingleOrDefaultAsync(ct);
        if (legal is null) throw LockUnproven();
        await LockAsync(db, legal, true, ct);
        await RequireGuardsAsync(db, ct);
    }

    internal static Task LockCommandAsync(CP6Context db, Guid key, CancellationToken ct)
    {
        var coordinate = $"Main.BP.Command/1:{db.CurrentTenantId:D}:{key:D}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({coordinate},0))", ct);
    }
}
