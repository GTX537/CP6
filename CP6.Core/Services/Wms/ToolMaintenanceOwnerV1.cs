using System.Globalization;
using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.ToolMaintenanceStoreV1;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
namespace CP6.Core.Services.Wms;

/// <summary>Real maintenance facts and restoration. Release qualification is supplied by the separate MASTER producer.</summary>
public sealed class ToolMaintenanceOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Wms.ToolMaintenance.commands.v1", Journal = "Main.Wms.ToolMaintenance.operations.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private sealed record Context(OperationsAccessV1 Access, OperationsAccessV1 Query, ToolInstanceStoreV1 Tools, ToolMaintenanceStoreV1 Store,
        ToolInstanceViewV1 Native, ToolRegistrationViewV1 Registration, ToolMaintenanceCycleV1 Cycle, ToolMaintenanceFactViewV1[] Facts)
    {
        internal string Etag => "\"tm1-" + Hash(JsonSerializer.Serialize(new { Native.Reference, Last = Facts.LastOrDefault()?.Reference }, ToolMaintenanceStoreV1.Json)) + "\"";
        internal bool Coherent => Registration.CycleKnowledge != "RECONCILIATION_REQUIRED";
    }
    private async Task<Context> Prepare(OperationsAccessV1 a, Guid id, bool write, CancellationToken ct)
    {
        if (id == Guid.Empty) throw Error("TOOL_INSTANCE_ID_INVALID", 400);
        var query = await a.SourceAsync(OperationsOwnerV1.ToolInstance, ct); var tools = new ToolInstanceStoreV1(db); await tools.Fence(ct, write);
        var store = new ToolMaintenanceStoreV1(db); await store.Fence(write, ct);
        var native = await tools.Read(query, id, null, ct);
        var registration = await new ToolRegistrationOwnerV1(db, owner).ReadOwnedAsync(query, id, ct) ?? throw Error("LEGACY_MAPPING_REQUIRED", 409);
        var facts = await store.Facts(id, ct);
        var original = JsonSerializer.Deserialize<ToolRegistrationOriginalV1>(registration.OriginalJson) ?? throw Error("TOOL_REGISTRATION_UNAVAILABLE", 503);
        var cycle = facts.LastOrDefault(x => x.Fact.Kind == "RESET")?.Fact.CycleAfter ?? new(original.CycleAnchorId, original.CycleNo,
            int.Parse(original.InitialCycleShots, CultureInfo.InvariantCulture), original.LifetimeKnowledge, original.LifetimeShots);
        cycle = (await ToolMaintenanceStoreV1.Project(db, original, ct)).Cycle;
        return new(a, query, tools, store, native, registration, cycle, facts);
    }
    private static void Text(string? text, int max, bool optional = false)
    {
        if (text == null && optional) return;
        if (string.IsNullOrWhiteSpace(text) || text.Length > max) throw Error("TOOL_MAINTENANCE_INPUT_INVALID", 400);
        try { new UTF8Encoding(false, true).GetByteCount(text); } catch (EncoderFallbackException) { throw Error("TOOL_MAINTENANCE_INPUT_INVALID", 400); }
    }
    private static void Ready(Context c)
    {
        if (!c.Coherent) throw Error("TOOL_CYCLE_RECONCILIATION_REQUIRED", 409);
        if (!c.Registration.CurrentSite.Enabled) throw Error("TOOL_SITE_DISABLED", 409);
        if (c.Native.Source.GetProperty("status").GetInt32() == 3) throw Error("TOOL_INSTANCE_DISCARDED", 409);
    }
    private static ToolMaintenanceEntryV1 Entry(Context c, Guid maintenance)
        => Entries(c.Facts).SingleOrDefault(x => x.MaintenanceId == maintenance) ?? throw Error("TOOL_MAINTENANCE_NOT_FOUND", 404);
    private async Task CurrentIssuer(ToolMaintenanceFactViewV1 fact, CancellationToken ct)
    {
        var witnesses = fact.Fact.Authorities;
        var requiredResource = fact.Fact.Kind == "POLICY" ? OperationsOwnerV1.ToolInstance : OperationsOwnerV1.PhysicalSite;
        var requiredAction = fact.Fact.Kind == "POLICY" ? "maintenance-policy" : "tool-maintenance-assess";
        if (witnesses.Length == 0 || !witnesses.Any(x => x.Resource == requiredResource && x.Action == requiredAction)
            || !witnesses.Any(x => x.Resource == OperationsOwnerV1.ToolInstance && x.Action == "query")
            || !witnesses.Any(x => x.Resource == OperationsOwnerV1.PhysicalSite && x.Action == "query")
            || witnesses.Select(x => (x.Resource, x.Action)).Distinct().Count() != witnesses.Length) throw Error("TOOL_MAINTENANCE_UNAVAILABLE", 503);
        foreach (var witness in witnesses)
        {
            var actual = await Authority.ToolMaintenanceReceiptAuthorityAsync(witness.Resource, witness.Action, fact.Fact.ActorId, ct);
            if (actual.Source.SourcePermissionVersion != witness.PermissionVersion || actual.OrganizationVersion != witness.OrganizationVersion)
                throw Error("TOOL_MAINTENANCE_ISSUER_CHANGED", 412);
            var access = new OperationsAccessV1(db, owner, actual);
            if (witness.Resource == OperationsOwnerV1.ToolInstance) await access.RequireToolInstance(db, fact.Fact.ToolId, ct);
            if (witness.Resource == OperationsOwnerV1.PhysicalSite && witness.Action == "query")
            {
                var site = await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, access, fact.Fact.Site.Id, ct);
                if (!site.Enabled || site.Reference != fact.Fact.Site) throw Error("TOOL_SITE_CHANGED", 412);
            }
            await access.ValidateFinalAsync(ct);
        }
    }
    private async Task<ToolMaintenancePolicyCommandV1> RequirePolicy(Context c, ToolMaintenanceRefV1 reference, bool final, CancellationToken ct)
    {
        var facts = final ? await c.Store.Facts(c.Native.Reference.Id, ct) : c.Facts;
        var value = Exact(facts, reference, "POLICY");
        if (facts.LastOrDefault(x => x.Fact.Kind == "POLICY")?.Reference != reference) throw Error("TOOL_MAINTENANCE_POLICY_CHANGED", 412);
        var p = Body<ToolMaintenancePolicyCommandV1>(value); var now = DateTimeOffset.UtcNow;
        if (p.State != "ENABLED" || now < p.EffectiveFrom || now >= p.EffectiveTo) throw Error("TOOL_MAINTENANCE_POLICY_UNAVAILABLE", 503);
        if (p.Site != c.Registration.CurrentSite.Reference || !c.Registration.CurrentSite.Enabled) throw Error("TOOL_SITE_CHANGED", 412);
        await CurrentIssuer(value, ct);
        return p;
    }
    public Task<ToolMaintenanceContextV1> ContextAsync(Guid id, int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 100000) throw Error("TOOL_MAINTENANCE_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, async (a, t) =>
        {
            var c = await Prepare(a, id, false, t); var p = c.Facts.LastOrDefault(x => x.Fact.Kind == "POLICY"); var knowledge = "MISSING";
            if (p != null) try { await RequirePolicy(c, p.Reference, false, t); knowledge = "CURRENT"; }
                catch (OrderPriceCorrectionException e) when (e.Status is 403 or 404 or 412 || e.Message == "TOOL_MAINTENANCE_POLICY_UNAVAILABLE") { knowledge = "UNAVAILABLE"; }
                catch (CurrentPermissionUnavailableException) { knowledge = "UNAVAILABLE"; }
            var entries = Entries(c.Facts).Reverse().Skip((page - 1) * 20).Take(20).ToArray(); var ids = entries.Select(x => x.MaintenanceId).ToHashSet();
            return new ToolMaintenanceContextV1(db.CurrentTenantId, id, c.Etag, c.Native.Reference, c.Registration.CurrentSite.Reference, c.Registration.CurrentSite.Enabled,
                new(CycleRef(c.Cycle), c.Coherent ? c.Cycle.Number : null, c.Coherent ? c.Cycle.Shots.ToString(CultureInfo.InvariantCulture) : null,
                    c.Coherent ? c.Registration.Origin : "RECONCILIATION_REQUIRED", c.Coherent ? c.Cycle.LifetimeKnowledge : "UNKNOWN", c.Coherent ? c.Cycle.LifetimeShots : null),
                p, knowledge, entries, c.Facts.Where(x => x.Fact.Kind == "ASSESSMENT" && ids.Contains(x.Fact.MaintenanceId!.Value)).ToArray(),
                c.Native.Source.GetProperty("status").GetInt32() == 1 || !c.Coherent || c.Registration.CurrentEligibility == "RESTRICTED" ? "RESTRICTED" : "UNKNOWN", Page: page);
        }, ct);
    }
    private async Task<ToolMaintenanceFactViewV1> Save(Context c, string kind, Guid? maintenance, object body, CancellationToken ct,
        ToolInstanceViewV1? after = null, ToolMaintenanceCycleV1? cycle = null)
    {
        var witnesses = c.Access.Authorities.DistinctBy(x => (x.Resource, x.Action)).Select(x => new ToolMaintenanceAuthorityV1(x.Resource, x.Action, x.Source.SourcePermissionVersion, x.OrganizationVersion)).ToArray();
        return await c.Store.Append(new(db.CurrentTenantId, c.Native.Reference.Id, Guid.NewGuid(), kind, maintenance, c.Access.ActorId, DateTimeOffset.UtcNow,
            c.Native.Reference, (after ?? c.Native).Reference, c.Registration.CurrentSite.Reference, witnesses, JsonSerializer.SerializeToElement(body, ToolMaintenanceStoreV1.Json), cycle), ct);
    }
    private Task<ToolMaintenanceResultV1> Write<T>(Guid id, string? etag, Guid operation, string action, T command,
        Func<Context, CancellationToken, Task<ToolMaintenanceFactViewV1>> change, CancellationToken ct, bool master = false, Guid? target = null)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        var bytes = MainOwnerOperationJournalV1.Encode(new { contract = Contract, id, etag, action, target, command });
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(master ? OperationsOwnerV1.PhysicalSite : OperationsOwnerV1.ToolInstance, action, true, async (a, t) =>
        {
            var c = await Prepare(a, id, true, t);
            if (original != null) return await Disclose(c, operation, original, bytes, t);
            if (string.IsNullOrEmpty(etag)) throw Error("IF_MATCH_REQUIRED", 428);
            if (etag != c.Etag) throw Error("TOOL_MAINTENANCE_CHANGED", 412);
            if (!master) await a.RequireToolInstance(db, id, t);
            var fact = await change(c, t);
            var outcome = fact.Fact.Kind switch { "COMPLETE" => fact.Fact.Body.GetProperty("outcome").GetString() == "PASS" ? "COMPLETED_PASS_PENDING_RESET" : fact.Fact.Body.GetProperty("outcome").GetString()!,
                "RESET" => "COMPLETED_PASS_RELEASED", "REPORT" => "RECORDED_MAINTENANCE_REPORT", "START" => "OPEN", _ => fact.Fact.Body.GetProperty(fact.Fact.Kind == "POLICY" ? "state" : "decision").GetString()! };
            var result = new ToolMaintenanceResultV1(operation, id, fact.Fact.Kind, fact.Reference, fact.Fact.MaintenanceId, outcome);
            await journal.Store(Journal, Contract, operation, a.ActorId, bytes, result, [id], new { action, a.ActorId, fact.Reference, fact.Fact.NativeBefore, fact.Fact.NativeAfter }, t);
            await c.Tools.HoldExactAsync(c.Query, fact.Fact.NativeAfter, t);
            a.AtCommit(async token =>
            {
                if ((await c.Store.Facts(id, token)).Last().Reference != fact.Reference) throw Error("TOOL_MAINTENANCE_CHANGED", 412);
                var sites = await c.Query.SourceAsync(OperationsOwnerV1.PhysicalSite, token);
                if ((await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, sites, c.Registration.CurrentSite.Reference.Id, token)).Reference != c.Registration.CurrentSite.Reference)
                    throw Error("TOOL_SITE_CHANGED", 412);
                // Recheck after policy/qualification callbacks as well as the earlier source participant fence.
                if ((await c.Tools.Read(c.Query, id, null, token)).Reference != fact.Fact.NativeAfter) throw Error("TOOL_INSTANCE_CHANGED", 412);
                if (!master) await a.RequireToolInstance(db, id, token);
            });
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Journal, operation, t); return original == null ? action : "query"; });
    }
    public Task<ToolMaintenanceResultV1> PolicyAsync(Guid id, string? etag, ToolMaintenancePolicyCommandV1 input, CancellationToken ct = default)
    {
        var p = OperationsOwnerV1.Freeze(input) with { EffectiveFrom = input.EffectiveFrom.ToUniversalTime(), EffectiveTo = input.EffectiveTo.ToUniversalTime() };
        Text(p.Reason, 1000);
        if (p.State is not ("ENABLED" or "REVOKED") || p.Site == null || p.RequiredChecks == null || p.RequiredChecks.Length is < 1 or > 30
            || p.RequiredChecks.Distinct(StringComparer.Ordinal).Count() != p.RequiredChecks.Length || p.EffectiveTo <= p.EffectiveFrom) throw Error("TOOL_MAINTENANCE_POLICY_INVALID", 400);
        foreach (var check in p.RequiredChecks) Text(check, 80);
        return Write(id, etag, p.OperationId, "maintenance-policy", p, async (c, t) =>
        {
            if (p.Site != c.Registration.CurrentSite.Reference) throw Error("TOOL_SITE_CHANGED", 412);
            if (p.State == "ENABLED") Ready(c);
            return await Save(c, "POLICY", null, p, t);
        }, ct);
    }
    public Task<ToolMaintenanceResultV1> StartAsync(Guid id, string? etag, ToolMaintenanceStartV1 input, CancellationToken ct = default)
    {
        var cmd = OperationsOwnerV1.Freeze(input); Text(cmd.Reason, 500);
        if (cmd.ReasonCode is not ("PLANNED" or "LIFE_REACHED" or "DAMAGE")) throw Error("TOOL_MAINTENANCE_REASON_INVALID", 400);
        DateTime? planned = null;
        if (cmd.PlannedCompletionDate != null)
        { if (!DateTime.TryParseExact(cmd.PlannedCompletionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) throw Error("TOOL_MAINTENANCE_DATE_INVALID", 400); planned = d; }
        return Write(id, etag, cmd.OperationId, "maintenance", cmd, async (c, t) =>
        {
            Ready(c); var entries = Entries(c.Facts); var last = entries.LastOrDefault(); var status = c.Native.Source.GetProperty("status").GetInt32();
            if (entries.Any(x => x.Status is "OPEN" or "COMPLETED_PASS_PENDING_RESET")) throw Error("TOOL_MAINTENANCE_PENDING", 409);
            if (status == 1)
            {
                if (last == null || cmd.PredecessorMaintenanceId != last.MaintenanceId || last.Status is not ("REPAIR_REQUIRED" or "UNSERVICEABLE")) throw Error("TOOL_MAINTENANCE_SUCCESSOR_REQUIRED", 409);
                if (last.Status == "UNSERVICEABLE")
                {
                    var policy = await RequirePolicy(c, cmd.RepairPolicy!, false, t);
                    var completionPolicy = last.Completion!.Fact.Body.GetProperty("policy").Deserialize<ToolMaintenanceRefV1>(ToolMaintenanceStoreV1.Json);
                    if (!policy.AllowUnserviceableRepair || cmd.RepairPolicy == completionPolicy
                        || completionPolicy == null && Exact(c.Facts, cmd.RepairPolicy!, "POLICY").Fact.RecordedAt <= last.Completion.Fact.RecordedAt)
                        throw Error("TOOL_REPAIR_POLICY_REQUIRED", 409);
                    c.Access.AtCommit(async token => { await RequirePolicy(c, cmd.RepairPolicy!, true, token); });
                }
                else if (cmd.RepairPolicy != null) throw Error("TOOL_MAINTENANCE_INPUT_INVALID", 400);
            }
            else if (status is not (0 or 2) || cmd.PredecessorMaintenanceId != null || cmd.RepairPolicy != null) throw Error("TOOL_MAINTENANCE_STATE_INVALID", 409);
            var row = await db.PlateMoldStocks.SingleAsync(x => x.Id == id, t); row.Status = 1; row.NextMaintenanceDate = planned;
            row.Modifier = c.Access.Actor; row.ModifyDate = DateTime.Now; await db.SaveChangesAsync(t);
            var after = await c.Tools.Read(c.Query, id, null, t);
            return await Save(c, "START", Guid.NewGuid(), new { cmd.ReasonCode, cmd.Reason, cmd.PlannedCompletionDate, cmd.PredecessorMaintenanceId, cmd.RepairPolicy, cycle = CycleRef(c.Cycle), c.Cycle.Number }, t, after);
        }, ct);
    }
    public Task<ToolMaintenanceResultV1> ReportAsync(Guid id, Guid maintenance, string? etag, ToolMaintenanceReportCommandV1 input, CancellationToken ct = default)
    {
        var cmd = OperationsOwnerV1.Freeze(input) with { OccurredAt = input.OccurredAt.ToUniversalTime() }; Text(cmd.Evidence, 2000);
        if (cmd.Outcome is not ("PASS" or "REPAIR_REQUIRED" or "UNSERVICEABLE") || cmd.Checks == null || cmd.Checks.Length is < 1 or > 30
            || cmd.Checks.Any(x => x == null) || cmd.Checks.Select(x => x.Code).Distinct(StringComparer.Ordinal).Count() != cmd.Checks.Length) throw Error("TOOL_MAINTENANCE_REPORT_INVALID", 400);
        foreach (var check in cmd.Checks) { Text(check.Code, 80); Text(check.Evidence, 1000); if (check.Result is not ("PASS" or "FAIL")) throw Error("TOOL_MAINTENANCE_REPORT_INVALID", 400); }
        return Write(id, etag, cmd.OperationId, "maintenance-record", cmd, async (c, t) =>
        {
            var entry = Entry(c, maintenance); if (entry.Status != "OPEN") throw Error("TOOL_MAINTENANCE_NOT_OPEN", 409);
            if (cmd.OccurredAt < entry.Start.Fact.RecordedAt || cmd.OccurredAt > DateTimeOffset.UtcNow) throw Error("TOOL_MAINTENANCE_TIME_INVALID", 422);
            if (cmd.Policy != null) Exact(c.Facts, cmd.Policy, "POLICY");
            return await Save(c, "REPORT", maintenance, cmd, t);
        }, ct, target: maintenance);
    }
    public Task<ToolMaintenanceResultV1> CompleteAsync(Guid id, Guid maintenance, string? etag, ToolMaintenanceCompleteV1 input, CancellationToken ct = default)
    {
        var cmd = OperationsOwnerV1.Freeze(input);
        return Write(id, etag, cmd.OperationId, "maintenance", cmd, async (c, t) =>
        {
            Ready(c); var entry = Entry(c, maintenance); if (entry.Status != "OPEN") throw Error("TOOL_MAINTENANCE_NOT_OPEN", 409);
            if (c.Native.Source.GetProperty("status").GetInt32() != 1) throw Error("TOOL_MAINTENANCE_STATE_INVALID", 409);
            var policy = await RequirePolicy(c, cmd.Policy, false, t); var report = Exact(c.Facts, cmd.EvidenceRef, "REPORT", maintenance);
            var body = Body<ToolMaintenanceReportCommandV1>(report);
            if (body.Policy != cmd.Policy || !policy.RequiredChecks.Order(StringComparer.Ordinal).SequenceEqual(body.Checks.Select(x => x.Code).Order(StringComparer.Ordinal))
                || body.Outcome == "PASS" && body.Checks.Any(x => x.Result != "PASS") || body.OccurredAt < policy.EffectiveFrom || body.OccurredAt >= policy.EffectiveTo
                || body.OccurredAt < Exact(c.Facts, cmd.Policy, "POLICY").Fact.RecordedAt) throw Error("TOOL_MAINTENANCE_EVIDENCE_MISMATCH", 422);
            if (policy.IndependentInspector && report.Fact.ActorId == c.Access.ActorId) throw Error("TOOL_MAINTENANCE_INDEPENDENT_INSPECTOR_REQUIRED", 403);
            c.Access.AtCommit(async token => { await RequirePolicy(c, cmd.Policy, true, token); });
            return await Save(c, "COMPLETE", maintenance, new { body.Outcome, body.OccurredAt, report = report.Reference, policy = cmd.Policy, cycle = CycleRef(c.Cycle) }, t);
        }, ct, target: maintenance);
    }
    internal Task<ToolMaintenanceResultV1> AssessAsync(Guid id, Guid maintenance, string? etag, ToolMaintenanceAssessmentCommandV1 input, CancellationToken ct)
    {
        var cmd = OperationsOwnerV1.Freeze(input) with { ValidUntil = input.ValidUntil.ToUniversalTime() }; Text(cmd.Evidence, 2000);
        if (cmd.Decision is not ("ELIGIBLE" or "RESTRICTED") || cmd.ValidUntil <= DateTimeOffset.UtcNow) throw Error("TOOL_MAINTENANCE_ASSESSMENT_INVALID", 400);
        return Write(id, etag, cmd.OperationId, "tool-maintenance-assess", cmd, async (c, t) =>
        {
            Ready(c); var entry = Entry(c, maintenance);
            if (entry.Status != "COMPLETED_PASS_PENDING_RESET" || entry.Completion!.Reference != cmd.CompletionRef) throw Error("TOOL_MAINTENANCE_COMPLETION_CHANGED", 412);
            var policy = await RequirePolicy(c, cmd.Policy, false, t);
            if (!policy.AllowReset || c.Cycle.Number == null) throw Error("TOOL_MAINTENANCE_RESET_UNQUALIFIED", 422);
            c.Access.AtCommit(async token => { await RequirePolicy(c, cmd.Policy, true, token); if (cmd.ValidUntil <= DateTimeOffset.UtcNow) throw Error("TOOL_MAINTENANCE_QUALIFICATION_CHANGED", 412); });
            return await Save(c, "ASSESSMENT", maintenance, cmd, t);
        }, ct, master: true, target: maintenance);
    }
    public Task<ToolMaintenanceResultV1> ResetAsync(Guid id, Guid maintenance, string? etag, ToolMaintenanceResetV1 input, CancellationToken ct = default)
    {
        var cmd = OperationsOwnerV1.Freeze(input);
        return Write(id, etag, cmd.OperationId, "maintenance-restore", cmd, async (c, t) =>
        {
            Ready(c); var entry = Entry(c, maintenance);
            if (entry.Status != "COMPLETED_PASS_PENDING_RESET" || entry.Completion!.Reference != cmd.CompletionRef) throw Error("TOOL_MAINTENANCE_COMPLETION_CHANGED", 412);
            if (cmd.ExpectedCycleRef != CycleRef(c.Cycle) || c.Cycle.Number == null) throw Error("TOOL_MAINTENANCE_CYCLE_UNPROVEN", 422);
            if (c.Native.Source.GetProperty("status").GetInt32() != 1) throw Error("TOOL_MAINTENANCE_STATE_INVALID", 409);
            var policy = await RequirePolicy(c, cmd.ResetPolicySelectionRef, false, t);
            if (!policy.AllowReset) throw Error("TOOL_MAINTENANCE_RESET_NOT_ALLOWED", 409);
            var assessment = Exact(c.Facts, cmd.QualificationRef, "ASSESSMENT", maintenance); var q = Body<ToolMaintenanceAssessmentCommandV1>(assessment);
            if (c.Facts.LastOrDefault(x => x.Fact.Kind == "ASSESSMENT" && x.Fact.MaintenanceId == maintenance)?.Reference != assessment.Reference
                || assessment.Fact.NativeAfter != c.Native.Reference || q.CompletionRef != cmd.CompletionRef || q.Policy != cmd.ResetPolicySelectionRef
                || q.Decision != "ELIGIBLE" || q.ValidUntil <= DateTimeOffset.UtcNow) throw Error("TOOL_MAINTENANCE_QUALIFICATION_CHANGED", 412);
            await CurrentIssuer(assessment, t);
            c.Access.AtCommit(async token =>
            {
                await RequirePolicy(c, cmd.ResetPolicySelectionRef, true, token); await CurrentIssuer(assessment, token);
                if (q.ValidUntil <= DateTimeOffset.UtcNow || (await c.Store.Facts(id, token)).LastOrDefault(x => x.Fact.Kind == "ASSESSMENT" && x.Fact.MaintenanceId == maintenance)?.Reference != assessment.Reference)
                    throw Error("TOOL_MAINTENANCE_QUALIFICATION_CHANGED", 412);
            });
            var number = Revision(c.Cycle.Number); if (number == long.MaxValue) throw Error("TOOL_CYCLE_OVERFLOW", 422);
            var cycle = new ToolMaintenanceCycleV1(Guid.NewGuid(), (number + 1).ToString(CultureInfo.InvariantCulture), 0, c.Cycle.LifetimeKnowledge, c.Cycle.LifetimeShots);
            var row = await db.PlateMoldStocks.SingleAsync(x => x.Id == id, t); row.UsedShots = 0; row.Status = 0; row.NextMaintenanceDate = null;
            row.Modifier = c.Access.Actor; row.ModifyDate = DateTime.Now; await db.SaveChangesAsync(t);
            var after = await c.Tools.Read(c.Query, id, null, t);
            return await Save(c, "RESET", maintenance, new { completion = cmd.CompletionRef, policy = cmd.ResetPolicySelectionRef, qualification = cmd.QualificationRef,
                previousCycle = c.Cycle, previousCycleRef = cmd.ExpectedCycleRef, qualificationAtEffect = "MAINTENANCE_RELEASE_ONLY" }, t, after, cycle);
        }, ct, target: maintenance);
    }
    // Called by the actual observation Owner inside the same authority fence and PostgreSQL transaction.
    // This creates the original maintenance completion; recovery policy and MASTER qualification stay separate.
    internal async Task<ToolMaintenanceFactViewV1> CompleteActualOwnedAsync(OperationsAccessV1 access,Guid id,
        ToolMaintenanceObservedBodyV1 observed,ToolActualRefV1 observation,ToolActualRefV1 decision,ToolActualRefV1 recognitionPolicy,CancellationToken ct)
    {
        if(!owner.Owns(db)||db.Database.CurrentTransaction==null)throw Error("AUTHORITY_FENCE_UNAVAILABLE",503);
        var c=await Prepare(access,id,true,ct);var maintenance=observed.Identity.SourceDocumentId;var entry=Entry(c,maintenance);
        var report=Exact(c.Facts,observed.EvidenceRef,"REPORT",maintenance);var body=Body<ToolMaintenanceReportCommandV1>(report);
        if(body.Outcome!=observed.Outcome||body.OccurredAt!=observed.OccurredAt)throw Error("OCCURRENCE_CONTENT_CONFLICT",409);
        if(entry.Completion is { } existing)
        {
            var b=existing.Fact.Body;
            if(b.GetProperty("report").Deserialize<ToolMaintenanceRefV1>(ToolMaintenanceStoreV1.Json)!=report.Reference
                ||b.GetProperty("outcome").GetString()!=body.Outcome||b.GetProperty("occurredAt").GetDateTimeOffset()!=body.OccurredAt)
                throw Error("TOOL_MAINTENANCE_COMPLETION_CONFLICT",409);
            return existing;
        }
        if(entry.Status!="OPEN"||c.Native.Source.GetProperty("status").GetInt32()!=1)throw Error("TOOL_MAINTENANCE_STATE_INVALID",409);
        return await Save(c,"COMPLETE",maintenance,new {body.Outcome,body.OccurredAt,report=report.Reference,policy=(ToolMaintenanceRefV1?)null,
            cycle=entry.Start.Fact.Body.GetProperty("cycle"),observation,decision,recognitionPolicy,qualificationAtOccurrence="UNPROVEN"},ct);
    }
    private async Task<ToolMaintenanceResultV1> Disclose(Context c, Guid operation, MainOwnerOperationJournalV1.Original original, byte[]? bytes, CancellationToken ct)
    {
        if (original.Actor != c.Access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (bytes != null && !MainOwnerOperationJournalV1.Matches(original, bytes)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ToolMaintenanceResultV1>(original.Result);
        if (original.State != "COMMITTED" || result == null || result.OperationId != operation || result.ObjectId != c.Native.Reference.Id
            || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var fact = Exact(c.Facts, result.Fact, result.Kind); await c.Tools.Read(c.Query, result.ObjectId, Revision(fact.Fact.NativeAfter.Version), ct);
        return result;
    }
    public Task<ToolMaintenanceOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", close, async (a, t) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Journal, id, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new ToolMaintenanceOperationV1(id, "CLOSED");
                if (original.Objects.Length != 1) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
                var c = await Prepare(a, original.Objects[0], close, t);
                return new(id, "COMMITTED", await Disclose(c, id, original, null, t));
            }
            if (!close) return new(id, "UNKNOWN");
            var result = new ToolMaintenanceOperationV1(id, "CLOSED"); await journal.Store(Journal, Contract, id, a.ActorId, null, result, [], new { action = "close-maintenance-operation" }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
    public Task<ToolMaintenanceFactViewV1> FactAsync(Guid toolId, Guid factId, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, async (a, t) =>
        {
            var c = await Prepare(a, toolId, false, t);
            var fact = c.Facts.SingleOrDefault(x => x.Reference.Id == factId) ?? throw Error("TOOL_MAINTENANCE_FACT_NOT_FOUND", 404);
            await c.Tools.Read(c.Query, toolId, Revision(fact.Fact.NativeBefore.Version), t);
            await c.Tools.Read(c.Query, toolId, Revision(fact.Fact.NativeAfter.Version), t);
            return fact;
        }, ct);
}

/// <summary>MASTER's own current qualification decision, not a caller-supplied approval receipt.</summary>
public sealed class ToolMaintenanceMasterOwnerV1(ToolMaintenanceOwnerV1 maintenance)
{
    public Task<ToolMaintenanceResultV1> AssessAsync(Guid id, Guid maintenanceId, string? etag, ToolMaintenanceAssessmentCommandV1 command, CancellationToken ct = default)
        => maintenance.AssessAsync(id, maintenanceId, etag, command, ct);
}
