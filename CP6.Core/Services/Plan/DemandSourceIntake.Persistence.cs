using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Plan;

internal sealed partial class DemandSourceIntake
{
    private sealed class Lineage
    {
        public Guid OrderId { get; init; }
        public Guid LineId { get; init; }
        public string ScheduleId { get; init; } = "";
        internal bool Matches(OrderSourceScope scope) =>
            OrderId == scope.OrderId && LineId == scope.LineId && ScheduleId == scope.ScheduleId;
    }

    internal sealed class SalesSource
    {
        public Guid ActorId { get; init; }
        public Guid OrderId { get; init; }
        public Guid LineId { get; init; }
        public string OrderVersion { get; init; } = "";
        public string ScheduleId { get; init; } = "";
        public string LineageId { get; init; } = "";
        public long SourceVersion { get; init; }
        public string SourceDigest { get; init; } = "";
        internal OrderSourceScope Scope => new(OrderId, LineId, OrderVersion, ScheduleId);
        internal bool Matches(SourceResultQuery query, long version) =>
            LineageId == query.ReleaseLineageId && SourceVersion == version && SourceDigest == query.SourceDigest;
    }

    internal sealed class Inbox
    {
        public string LineageId { get; init; } = "";
        public long SourceVersion { get; init; }
        public string SourceDigest { get; init; } = "";
        public string BodySha256 { get; init; } = "";
        public Guid ResultId { get; init; }
        public Guid ActorId { get; init; }
    }

    private sealed class Demand
    {
        public Guid DemandId { get; init; }
        public long DemandVersion { get; init; }
        public long SourceVersion { get; init; }
        public string SourceDigest { get; init; } = "";
        public Guid OrderId { get; init; }
        public Guid LineId { get; init; }
        public string OrderVersion { get; init; } = "";
        public string ScheduleId { get; init; } = "";
        public byte[] Target { get; init; } = [];
    }

    private sealed class ScopeControl
    {
        public string OrderVersion { get; init; } = "";
        public bool Plans { get; init; }
        public long EffectiveAtTicks { get; init; }
        public string Disposition { get; init; } = "";
        public byte[] Fact { get; init; } = [];
    }

    private sealed class Observation
    {
        public long Version { get; init; }
        public long Count { get; init; }
    }

    private Task Lock(string slot, string coordinate, CancellationToken ct)
    {
        var key = JsonSerializer.Serialize(new[] { Owner, Contract, db.CurrentTenantId.ToString("D"), Producer, slot, coordinate });
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
    }

    private Task<Lineage?> FindLineage(string lineage, CancellationToken ct) =>
        db.Database.SqlQuery<Lineage>($"""
            SELECT order_id AS "OrderId",line_id AS "LineId",schedule_id AS "ScheduleId"
            FROM cp6_planning_demand_source_v1.lineages
            WHERE tenant_id={db.CurrentTenantId} AND producer={Producer} AND lineage_id={lineage}
            """).SingleOrDefaultAsync(ct);

    // 披露授权前只读取固定 Sales 来源的作用域坐标。
    private Task<SalesSource?> FindSalesOrigin(string operation, CancellationToken ct) =>
        db.Database.SqlQuery<SalesSource>($"""
            SELECT actor_id AS "ActorId",order_id AS "OrderId",line_id AS "LineId",order_version AS "OrderVersion",
                schedule_id AS "ScheduleId",lineage_id AS "LineageId",source_version AS "SourceVersion",source_digest AS "SourceDigest"
            FROM cp6_sales_order_source_v1.operations
            WHERE tenant_id={db.CurrentTenantId} AND producer={Producer} AND operation_key={operation}
            """).SingleOrDefaultAsync(ct);

    private Task<Inbox?> FindInbox(string operation, CancellationToken ct) =>
        db.Database.SqlQuery<Inbox>($"""
            SELECT inbox.lineage_id AS "LineageId",inbox.source_version AS "SourceVersion",inbox.source_digest AS "SourceDigest",
                inbox.body_sha256 AS "BodySha256",inbox.result_id AS "ResultId",original.actor_id AS "ActorId"
            FROM cp6_planning_demand_source_v1.inbox inbox
            JOIN cp6_main_owner_v1.operations original
              ON (original.tenant_id,original.owner,original.operation_id)=(inbox.tenant_id,inbox.result_owner,inbox.result_id)
            WHERE inbox.tenant_id={db.CurrentTenantId} AND inbox.producer={Producer} AND inbox.operation_key={operation}
            """).SingleOrDefaultAsync(ct);

    private Task<bool> ObservedVersion(string lineage, long version, CancellationToken ct) =>
        db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT FROM cp6_planning_demand_source_v1.inbox
                WHERE tenant_id={db.CurrentTenantId} AND producer={Producer}
                  AND lineage_id={lineage} AND source_version={version}) AS "Value"
            """).SingleAsync(ct);

    private async Task<MainOwnerOperationJournalV1.Original> ReadOriginal(Inbox inbox, SourceResultQuery query,
        byte[]? input, CancellationToken ct)
    {
        if (inbox.LineageId != query.ReleaseLineageId || Revision(inbox.SourceVersion) != query.SourceVersion ||
            inbox.SourceDigest != query.SourceDigest) throw Conflict();
        var original = await new MainOwnerOperationJournalV1(db).Read(Owner, inbox.ResultId, ct);
        if (original is not { Contract: Contract, State: "COMMITTED", Input: not null } ||
            original.Actor != inbox.ActorId || original.Digest != inbox.BodySha256 ||
            MainOwnerOperationJournalV1.Digest(original.Input) != inbox.BodySha256)
            throw Unproven();
        if (input is not null && !original.Input.AsSpan().SequenceEqual(input)) throw Conflict();
        return original;
    }

    private Task<Demand?> FindDemand(string lineage, CancellationToken ct) =>
        db.Database.SqlQuery<Demand>($"""
            SELECT binding.demand_id AS "DemandId",binding.demand_version AS "DemandVersion",
                binding.source_version AS "SourceVersion",inbox.source_digest AS "SourceDigest",binding.order_version AS "OrderVersion",
                lineage.order_id AS "OrderId",lineage.line_id AS "LineId",lineage.schedule_id AS "ScheduleId",version.target AS "Target"
            FROM cp6_planning_demand_source_v1.source_bindings binding
            JOIN cp6_planning_demand_source_v1.inbox inbox USING (tenant_id,producer,lineage_id,source_version)
            JOIN cp6_planning_demand_source_v1.lineages lineage USING (tenant_id,producer,lineage_id)
            JOIN cp6_planning_demand_source_v1.demand_versions version USING (tenant_id,demand_id,demand_version)
            WHERE binding.tenant_id={db.CurrentTenantId} AND binding.producer={Producer} AND binding.lineage_id={lineage}
            """).SingleOrDefaultAsync(ct);

    private Task<ScopeControl[]> ReadControls(string lineage, CancellationToken ct) =>
        db.Database.SqlQuery<ScopeControl>($"""
            SELECT order_version AS "OrderVersion",effective_at_ticks AS "EffectiveAtTicks",disposition AS "Disposition",
                plans AS "Plans",fact AS "Fact"
            FROM cp6_planning_demand_source_v1.controls
            WHERE tenant_id={db.CurrentTenantId} AND producer={Producer} AND lineage_id={lineage}
            ORDER BY source_version,position
            """).ToArrayAsync(ct);

    private async Task<DemandCurrent> ReadCurrent(string lineage, IDbContextTransaction tx, DateTimeOffset now, CancellationToken ct)
    {
        var observation = await db.Database.SqlQuery<Observation>($"""
            SELECT max(source_version) AS "Version",count(*) AS "Count"
            FROM cp6_planning_demand_source_v1.inbox
            WHERE tenant_id={db.CurrentTenantId} AND producer={Producer} AND lineage_id={lineage}
            """).SingleAsync(ct);
        var controls = await ReadControls(lineage, ct);
        var facts = controls.Take(MaxControls).Select(control => JsonSerializer.Deserialize<JsonElement>(control.Fact)).ToArray();
        var uncertainty = controls.Length > MaxControls ? "CONTROL_COVERAGE_UNPROVEN"
            : observation.Count != observation.Version ? "SOURCE_HISTORY_UNPROVEN" : null;
        var demand = await FindDemand(lineage, ct);
        if (demand is null)
            return new(lineage, null, null, null, null, facts, "NONE", [], Watermark(lineage, observation.Version, now, uncertainty));

        var scope = new OrderSourceScope(demand.OrderId, demand.LineId, demand.OrderVersion, demand.ScheduleId);
        var upstream = await new OrderSourceParticipant(db, clock).CheckCurrentSource(tx.GetDbTransaction(),
            scope, lineage, demand.SourceVersion, demand.SourceDigest, OrderSourceAction.Plan, ct);
        var stopped = ControlsForPlanning(controls, demand.OrderVersion, now);
        var terminated = stopped.Any(control => control.Disposition == "TERMINATE");
        if (stopped.Length != 0 && !terminated) uncertainty = "SOURCE_CONTROL_BLOCKED";
        else if (!terminated && upstream.State != "CURRENT") uncertainty ??= "SALES_EFFECTIVITY_UNPROVEN";
        var effective = !terminated && uncertainty is null;
        return new(lineage, Revision(demand.SourceVersion), demand.DemandId, Revision(demand.DemandVersion),
            effective ? JsonSerializer.Deserialize<JsonElement>(demand.Target) : null, facts,
            terminated ? "TERMINATED" : effective ? "EFFECTIVE" : "UNKNOWN", [],
            Watermark(lineage, observation.Version, now, uncertainty));
    }

    private Task AddLineage(OrderSourceReceipt source, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_planning_demand_source_v1.lineages (tenant_id,producer,lineage_id,order_id,line_id,schedule_id)
            VALUES ({db.CurrentTenantId},{Producer},{source.LineageId},{source.Scope.OrderId},{source.Scope.LineId},{source.Scope.ScheduleId})
            """, ct);

    private Task AddInbox(OrderSourceReceipt source, OrderSourceParticipant.Saved original, Guid result, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO cp6_planning_demand_source_v1.inbox
                (tenant_id,producer,operation_key,lineage_id,source_version,source_digest,body_sha256,result_id,sales_receipt,sales_envelope)
            VALUES ({db.CurrentTenantId},{Producer},{source.OperationKey},{source.LineageId},{source.SourceVersion},
                {source.SourceDigest},{source.BodySha256},{result},{original.Result},{original.Envelope})
            """, ct);

    private Task AddDemand(OrderSourceReceipt source, Guid demand, JsonElement target, DateTimeOffset acceptedAt, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH root AS (
                INSERT INTO cp6_planning_demand_source_v1.demands (tenant_id,demand_id,producer,lineage_id)
                VALUES ({db.CurrentTenantId},{demand},{Producer},{source.LineageId}) RETURNING tenant_id,demand_id
            ), version AS (
                INSERT INTO cp6_planning_demand_source_v1.demand_versions (tenant_id,demand_id,demand_version,target,accepted_at)
                SELECT tenant_id,demand_id,1,{JsonBytes(target)},{acceptedAt} FROM root
                RETURNING tenant_id,demand_id,demand_version
            )
            INSERT INTO cp6_planning_demand_source_v1.source_bindings
                (tenant_id,producer,lineage_id,source_version,demand_id,demand_version,order_version,release_id,release_line_id)
            SELECT tenant_id,{Producer},{source.LineageId},{source.SourceVersion},demand_id,demand_version,
                {source.Scope.OrderVersion},{source.ReleaseId},{source.ReleaseLineId} FROM version
            """, ct);

    private async Task AddControls(OrderSourceReceipt source, Control[] controls, CancellationToken ct)
    {
        for (var i = 0; i < controls.Length; i++)
        {
            var fact = controls[i];
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_planning_demand_source_v1.controls
                    (tenant_id,producer,lineage_id,source_version,position,order_version,effective_at_ticks,disposition,plans,fact)
                VALUES ({db.CurrentTenantId},{Producer},{source.LineageId},{source.SourceVersion},{i},
                    {source.Scope.OrderVersion},{fact.EffectiveAt.UtcTicks},{fact.Disposition},{fact.Plans},{JsonBytes(fact.Fact)})
                """, ct);
        }
    }
}
