using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

/// <summary>
/// Production work-slot queries on the caller's tenant and current transaction.
/// Complete acquisition additionally holds TenantLockResource across initialization, count and write.
/// </summary>
public static class SpaceAiWorkSlotQueries
{
    public static string TenantLockResource(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        return $"cp6:space:ai-work-slots:{tenantId:N}";
    }

    public static async Task EnsureAsync(SpaceContext context, Guid tenantId, CancellationToken cancellationToken = default)
    {
        var postgres = RequireProtocol(context, tenantId);
        for (var slotNo = 1; slotNo <= SpaceTenantAiWorkSlot.PlatformSlotCount; slotNo++)
        {
            if (postgres)
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO public."Space_TenantAiWorkSlot"
                        ("TenantId", "SlotNo", "RunId", "LeaseOwner", "LeaseExpiresAtUtc")
                    VALUES ({tenantId}, {slotNo}, NULL, NULL, NULL)
                    ON CONFLICT ("TenantId", "SlotNo") DO NOTHING
                    """, cancellationToken);
            else
                await context.Database.ExecuteSqlInterpolatedAsync($"""
                    IF NOT EXISTS (
                        SELECT 1 FROM [dbo].[Space_TenantAiWorkSlot] WITH (UPDLOCK, HOLDLOCK)
                        WHERE [TenantId] = {tenantId} AND [SlotNo] = {slotNo})
                    BEGIN
                        INSERT INTO [dbo].[Space_TenantAiWorkSlot]
                            ([TenantId], [SlotNo], [RunId], [LeaseOwner], [LeaseExpiresAtUtc])
                        VALUES ({tenantId}, {slotNo}, NULL, NULL, NULL)
                    END
                    """, cancellationToken);
        }
    }

    public static async Task<SpaceTenantAiWorkSlot?> FindExistingAsync(
        SpaceContext context, Guid tenantId, Guid runId, CancellationToken cancellationToken = default)
    {
        var postgres = RequireProtocol(context, tenantId);
        if (runId == Guid.Empty) throw new ArgumentException("Run is required.", nameof(runId));
        var query = postgres
            ? context.TenantAiWorkSlots.FromSqlInterpolated($"""
                SELECT * FROM public."Space_TenantAiWorkSlot"
                WHERE "TenantId" = {tenantId} AND "RunId" = {runId}
                FOR UPDATE
                """)
            : context.TenantAiWorkSlots.FromSqlInterpolated($"""
                SELECT * FROM [dbo].[Space_TenantAiWorkSlot] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
                WHERE [TenantId] = {tenantId} AND [RunId] = {runId}
                """);
        return (await query.AsTracking().ToListAsync(cancellationToken)).SingleOrDefault();
    }

    public static async Task<int> CountActiveAsync(
        SpaceContext context, Guid tenantId, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        var postgres = RequireProtocol(context, tenantId);
        RequireUtc(nowUtc);
        if (postgres)
            return await context.TenantAiWorkSlots.AsNoTracking().CountAsync(
                slot => slot.TenantId == tenantId && slot.RunId != null && slot.LeaseExpiresAtUtc > nowUtc,
                cancellationToken);
        // The whole-acquire tenant mutex protects the count and first creation on both providers.
        // SQL retains its original row/range-lock hints; no aggregate is composed over PG FOR UPDATE.
        var rows = await context.TenantAiWorkSlots.FromSqlInterpolated($"""
            SELECT * FROM [dbo].[Space_TenantAiWorkSlot] WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
            WHERE [TenantId] = {tenantId} AND [RunId] IS NOT NULL AND [LeaseExpiresAtUtc] > {nowUtc}
            """).AsNoTracking().ToListAsync(cancellationToken);
        return rows.Count;
    }

    public static async Task<SpaceTenantAiWorkSlot?> FindAvailableAsync(
        SpaceContext context, Guid tenantId, int maxConcurrentRuns, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        var postgres = RequireProtocol(context, tenantId);
        if (maxConcurrentRuns is < 1 or > SpaceTenantAiWorkSlot.PlatformSlotCount)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentRuns));
        RequireUtc(nowUtc);
        var query = postgres
            ? context.TenantAiWorkSlots.FromSqlInterpolated($"""
                SELECT * FROM public."Space_TenantAiWorkSlot"
                WHERE "TenantId" = {tenantId} AND "SlotNo" <= {maxConcurrentRuns}
                  AND ("RunId" IS NULL OR "LeaseExpiresAtUtc" <= {nowUtc})
                ORDER BY "SlotNo" LIMIT 1 FOR UPDATE SKIP LOCKED
                """)
            : context.TenantAiWorkSlots.FromSqlInterpolated($"""
                SELECT TOP (1) * FROM [dbo].[Space_TenantAiWorkSlot] WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE [TenantId] = {tenantId} AND [SlotNo] <= {maxConcurrentRuns}
                  AND ([RunId] IS NULL OR [LeaseExpiresAtUtc] <= {nowUtc})
                ORDER BY [SlotNo]
                """);
        var originalTimeout = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(originalTimeout is > 0 ? Math.Min(originalTimeout.Value, 30) : 30);
        try
        {
            return (await query.AsTracking().ToListAsync(cancellationToken)).SingleOrDefault();
        }
        finally
        {
            context.Database.SetCommandTimeout(originalTimeout);
        }
    }

    private static bool RequireProtocol(SpaceContext context, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (tenantId == Guid.Empty || tenantId != context.CurrentTenantId)
            throw new SpaceTenantScopeException("AI work-slot queries require the current Space tenant.");
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A caller transaction is required for AI work-slot queries.");
        if (context.Database.IsNpgsql()) return true;
        if (context.Database.IsSqlServer()) return false;
        throw new InvalidOperationException("AI work-slot queries require SQL Server or PostgreSQL.");
    }

    private static void RequireUtc(DateTime nowUtc)
    {
        if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("The work-slot clock must be UTC.", nameof(nowUtc));
    }
}
