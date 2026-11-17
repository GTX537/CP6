using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public sealed record FinanceScopeV1(string EnvironmentId, Guid TenantId, string LegalEntityKey, string LedgerKey, string Mode);
public sealed record FinanceContextChoiceV1(Guid Token, string LegalEntityLabel, string LedgerLabel);
public sealed record CostCenterVersionLocatorV1(Guid RootId, Guid VersionId, Guid OpaqueToken);
public sealed record CostCenterSelectionV1(CostCenterVersionLocatorV1 Locator, string Code, string Name,
    DateTimeOffset AsOf)
{
    public string Kind => "COST_CENTER";
    public string Availability => "SELECTABLE";
    public string? Reason => null;
    public bool IsExecutionPermit => false;
}

public sealed class ManufacturingFinanceScopeV1(CP6Context db, string? environmentId)
{
    internal static OrderPriceCorrectionException Error(string code, int status) => new(code, status);

    internal Task<T> WithContextAsync<T>(OperationsOwnerV1 owner, Guid context, string action, bool write,
        Func<LedgerRow, OperationsAccessV1, CancellationToken, Task<T>> body, CancellationToken ct)
        => owner.ExecuteAsync(OperationsOwnerV1.FinCost, action, true, async (access, token) =>
        {
            if (write)
                owner.RequireOriginalFinanceActor();
            await GateAsync(write, token);
            var ledger = await RequireContextAsync(context, access.ActorId, token);
            access.AtCommit(async final => { await RequireContextAsync(context, access.ActorId, final); });
            return await body(ledger, access, token);
        }, ct);

    internal Task GateAsync(bool write, CancellationToken ct)
    {
        // Trusted provisioning and the native owner must share the same PostgreSQL transaction fences.
        return db.Database.ExecuteSqlRawAsync(write
            ? """
                LOCK TABLE public."Fin_CostCenter", cp6_fin_mfg_v1.ledgers, cp6_fin_mfg_v1.members, cp6_fin_mfg_v1.center_versions,
                    cp6_fin_mfg_v1.policies, cp6_fin_mfg_v1.rate_bases, cp6_fin_mfg_v1.qualifications,
                    cp6_fin_mfg_v1.work_orders, cp6_fin_mfg_v1.rate_heads, cp6_fin_mfg_v1.rate_versions,
                    cp6_fin_mfg_v1.timeline, cp6_fin_mfg_v1.standard_basis_versions, cp6_fin_mfg_v1.standard_basis_heads
                    IN SHARE ROW EXCLUSIVE MODE
                """
            : """
                LOCK TABLE public."Fin_CostCenter", cp6_fin_mfg_v1.ledgers, cp6_fin_mfg_v1.members, cp6_fin_mfg_v1.center_versions,
                    cp6_fin_mfg_v1.policies, cp6_fin_mfg_v1.rate_bases, cp6_fin_mfg_v1.qualifications,
                    cp6_fin_mfg_v1.work_orders, cp6_fin_mfg_v1.rate_heads, cp6_fin_mfg_v1.rate_versions,
                    cp6_fin_mfg_v1.timeline, cp6_fin_mfg_v1.standard_basis_versions, cp6_fin_mfg_v1.standard_basis_heads
                    IN SHARE MODE
                """, ct);
    }

    internal async Task<LedgerRow[]> ListAsync(Guid subject, CancellationToken ct)
        => string.IsNullOrWhiteSpace(environmentId) ? [] : await db.Database.SqlQuery<LedgerRow>($"""
            SELECT l.id AS "Id",l.environment_id AS "EnvironmentId",l.legal_entity_key AS "LegalEntityKey",
                l.ledger_key AS "LedgerKey",l.currency AS "Currency",l.business_date AS "BusinessDate"
            FROM cp6_fin_mfg_v1.ledgers l JOIN cp6_fin_mfg_v1.members m
                ON (m.tenant_id,m.ledger_id)=(l.tenant_id,l.id)
            WHERE l.tenant_id={db.CurrentTenantId} AND l.environment_id={environmentId}
                AND l.mode='REAL' AND l.enabled AND m.enabled AND m.subject_id={subject}
            ORDER BY l.legal_entity_key,l.ledger_key
            """).ToArrayAsync(ct);

    internal async Task<Guid> IssueContextAsync(Guid ledger, Guid subject, CancellationToken ct)
    {
        var issued = await db.Database.SqlQuery<Guid>($"""
            INSERT INTO cp6_fin_mfg_v1.contexts AS context (token,tenant_id,ledger_id,subject_id,expires_at)
            VALUES ({Guid.NewGuid()},{db.CurrentTenantId},{ledger},{subject},clock_timestamp()+interval '15 minutes')
            ON CONFLICT (tenant_id,ledger_id,subject_id) DO UPDATE SET
              token=CASE WHEN context.expires_at>clock_timestamp() THEN context.token ELSE EXCLUDED.token END,
              expires_at=GREATEST(context.expires_at,EXCLUDED.expires_at)
            RETURNING token AS "Value"
            """).ToListAsync(ct);
        return issued.Single();
    }

    internal async Task<LedgerRow> RequireContextAsync(Guid token, Guid subject, CancellationToken ct)
        => await db.Database.SqlQuery<LedgerRow>($"""
            SELECT l.id AS "Id",l.environment_id AS "EnvironmentId",l.legal_entity_key AS "LegalEntityKey",
                l.ledger_key AS "LedgerKey",l.currency AS "Currency",l.business_date AS "BusinessDate"
            FROM cp6_fin_mfg_v1.contexts c
            JOIN cp6_fin_mfg_v1.members m ON (m.tenant_id,m.ledger_id,m.subject_id)=(c.tenant_id,c.ledger_id,c.subject_id)
            JOIN cp6_fin_mfg_v1.ledgers l ON (l.tenant_id,l.id)=(c.tenant_id,c.ledger_id)
            WHERE c.tenant_id={db.CurrentTenantId} AND c.token={token} AND c.subject_id={subject}
              AND c.expires_at>clock_timestamp() AND m.enabled AND l.enabled AND l.mode='REAL' AND l.environment_id={environmentId}
            """).SingleOrDefaultAsync(ct) ?? throw Error("FINANCE_CONTEXT_NOT_FOUND", 404);

    internal async Task<LedgerRow?> AssignedLedgerAsync(Guid workOrder, Guid subject, CancellationToken ct)
    {
        // Unassigned historical/current A2 work orders keep their established input source.
        var assigned = await db.Database.SqlQuery<Guid>($"""
            SELECT ledger_id AS "Value" FROM cp6_fin_mfg_v1.work_orders
            WHERE tenant_id={db.CurrentTenantId} AND work_order_id={workOrder}
            """).ToListAsync(ct);
        if (assigned.Count == 0)
            return null;
        var ledgers = await ListAsync(subject, ct);
        return ledgers.SingleOrDefault(row => row.Id == assigned[0])
            ?? throw Error("FINANCE_CONTEXT_NOT_FOUND", 404);
    }

    internal async Task<bool> CanUseLedgerAsync(Guid ledger, Guid subject, CancellationToken ct)
        => await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.members m JOIN cp6_fin_mfg_v1.ledgers l
              ON (l.tenant_id,l.id)=(m.tenant_id,m.ledger_id)
              WHERE m.tenant_id={db.CurrentTenantId} AND m.ledger_id={ledger} AND m.subject_id={subject}
                AND m.enabled AND l.enabled AND l.mode='REAL' AND l.environment_id={environmentId}) AS "Value"
            """).SingleAsync(ct);

    internal async Task<Guid> QualificationAsync(Guid ledger, Guid subject, string action, CancellationToken ct)
        => await db.Database.SqlQuery<Guid?>($"""
            SELECT q.qualification_id AS "Value" FROM cp6_fin_mfg_v1.qualifications q
            JOIN cp6_fin_mfg_v1.members m USING(tenant_id,ledger_id,subject_id)
            WHERE q.tenant_id={db.CurrentTenantId} AND q.ledger_id={ledger} AND q.subject_id={subject} AND q.action={action}
                AND q.enabled AND m.enabled AND q.expires_at>clock_timestamp()
                AND (q.action IN ('rate-review','rate-release') OR EXISTS(
                    SELECT 1 FROM cp6_fin_mfg_v1.qualification_decisions decision
                    WHERE (decision.tenant_id,decision.ledger_id,decision.subject_id,decision.action,decision.id)=
                          (q.tenant_id,q.ledger_id,q.subject_id,q.action,q.qualification_id) AND decision.enabled))
            """).SingleOrDefaultAsync(ct) ?? throw Error("FINANCE_QUALIFICATION_REQUIRED", 403);

    internal IQueryable<CostSheet> CostSheetsInScope(IQueryable<CostSheet> sheets, Guid subject)
    {
        var assignments = db.Database.SqlQuery<WorkOrderScopeRow>($"""
            SELECT w."WorkOrderNo" AS "WorkOrderNo",
              (l.enabled AND l.mode='REAL' AND l.environment_id={environmentId} AND EXISTS(
                  SELECT 1 FROM cp6_fin_mfg_v1.members m
                  WHERE m.tenant_id=link.tenant_id AND m.ledger_id=link.ledger_id AND m.subject_id={subject} AND m.enabled
              )) IS TRUE AS "Visible"
            FROM cp6_fin_mfg_v1.work_orders link
            JOIN cp6_fin_mfg_v1.ledgers l ON (l.tenant_id,l.id)=(link.tenant_id,link.ledger_id)
            JOIN public."T_WorkOrder" w ON w."TenantId"=link.tenant_id AND w."Id"=link.work_order_id
            WHERE link.tenant_id={db.CurrentTenantId}
            """);
        return sheets.Where(sheet => !assignments.Any(row => row.WorkOrderNo == sheet.WorkOrderNo && !row.Visible));
    }

    internal async Task<CostCenterSelectionV1[]> ListCentersAsync(Guid ledger, Guid subject, DateOnly date, CancellationToken ct)
    {
        var centers = await db.Database.SqlQuery<CenterRow>($"""
            SELECT v.root_id AS "RootId",v.version_id AS "VersionId",v.code AS "Code",v.name AS "Name"
            FROM cp6_fin_mfg_v1.current_center_versions v
            WHERE v.tenant_id={db.CurrentTenantId} AND v.ledger_id={ledger}
              AND v.effective_dates @> {date}
            ORDER BY v.code,v.root_id,v.version DESC
            """).ToArrayAsync(ct);
        if (centers.GroupBy(row => row.RootId).Any(group => group.Count() != 1))
            throw Error("COST_CENTER_VERSION_CONFLICT", 409);
        var result = new List<CostCenterSelectionV1>(centers.Length);
        foreach (var center in centers)
        {
            var tokens = await db.Database.SqlQuery<Guid>($"""
                INSERT INTO cp6_fin_mfg_v1.center_selections AS selection
                    (token,tenant_id,ledger_id,subject_id,root_id,version_id,expires_at)
                VALUES ({Guid.NewGuid()},{db.CurrentTenantId},{ledger},{subject},{center.RootId},{center.VersionId},
                    clock_timestamp()+interval '15 minutes')
                ON CONFLICT (tenant_id,ledger_id,subject_id,root_id,version_id) DO UPDATE SET
                    token=CASE WHEN selection.expires_at>clock_timestamp() THEN selection.token ELSE EXCLUDED.token END,
                    expires_at=GREATEST(selection.expires_at,EXCLUDED.expires_at)
                RETURNING token AS "Value"
                """).ToListAsync(ct);
            result.Add(new(new(center.RootId, center.VersionId, tokens.Single()), center.Code, center.Name, DateTimeOffset.UtcNow));
        }
        return result.ToArray();
    }

    internal async Task RequireLocatorAsync(Guid ledger, Guid subject, CostCenterVersionLocatorV1 locator, CancellationToken ct)
    {
        if (!await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS(SELECT 1 FROM cp6_fin_mfg_v1.center_selections
                WHERE token={locator.OpaqueToken} AND tenant_id={db.CurrentTenantId} AND ledger_id={ledger}
                  AND subject_id={subject} AND root_id={locator.RootId} AND version_id={locator.VersionId}
                  AND expires_at>clock_timestamp()) AS "Value"
            """).SingleAsync(ct))
            throw Error("COST_CENTER_VERSION_NOT_FOUND", 404);
    }

    internal sealed class LedgerRow
    {
        public Guid Id { get; set; }
        public string EnvironmentId { get; set; } = null!;
        public string LegalEntityKey { get; set; } = null!;
        public string LedgerKey { get; set; } = null!;
        public string Currency { get; set; } = null!;
        public DateOnly BusinessDate { get; set; }
        internal FinanceScopeV1 Scope(Guid tenant) => new(EnvironmentId, tenant, LegalEntityKey, LedgerKey, "REAL");
    }
    internal sealed class CenterRow
    {
        public Guid RootId { get; set; }
        public Guid VersionId { get; set; }
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
    }
    internal sealed class WorkOrderScopeRow
    {
        public string WorkOrderNo { get; set; } = null!;
        public bool Visible { get; set; }
    }
}
