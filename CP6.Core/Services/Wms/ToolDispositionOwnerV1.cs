using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
using static CP6.Core.Services.Wms.ToolMaintenanceStoreV1;
namespace CP6.Core.Services.Wms;

/// <summary>Original disposition intent and explicit block. Approval and cross-Owner closure are separate future consumers.</summary>
public sealed class ToolDispositionOwnerV1(CP6Context db, OperationsOwnerV1 owner)
{
    private const string Contract = "Main.Wms.ToolDisposition.commands.v1", Journal = "Main.Wms.ToolDisposition.operations.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private sealed record Context(OperationsAccessV1 Access, OperationsAccessV1 Query, ToolInstanceStoreV1 Tools, ToolDispositionStoreV1 Store,
        ToolInstanceViewV1 Native, ToolRegistrationViewV1 Registration, ToolDispositionFactViewV1[] Facts, ToolDispositionObligationsV1 Obligations)
    {
        internal string Etag => "\"td1-" + Hash(JsonSerializer.Serialize(new { Native = Native.Reference, Site = Registration.CurrentSite.Reference, Last = Facts.LastOrDefault()?.Reference, Obligations.Version }, ToolMaintenanceStoreV1.Json)) + "\"";
    }
    private async Task<ToolDispositionObligationsV1> Obligations(Guid id, CancellationToken ct)
    {
        var maintenance = new ToolMaintenanceStoreV1(db); await maintenance.Fence(false, ct);
        var actual = new ToolActualUsageStoreV1(db); await actual.Fence(false, ct);
        var m = await maintenance.Facts(id, ct); var a = await actual.Facts(id, ct);
        var local = new List<ToolDispositionObligationV1>();
        foreach (var entry in Entries(m).Where(x => x.Status != "COMPLETED_PASS_RELEASED"))
        {
            var r = (entry.Completion ?? entry.Start).Reference;
            local.Add(new(r.Owner, r.Id.ToString(), r.Version, r.Digest, "MAINTENANCE", entry.Status));
        }
        foreach (var fact in a.Where(x => x.Fact.Kind is "OBSERVATION" or "M_OBSERVATION"))
        {
            var applied = a.Any(x => x.Fact.Kind == (fact.Fact.Kind == "OBSERVATION" ? "APPLICATION" : "M_APPLICATION") && x.Fact.SubjectId == fact.Reference.Id);
            var r = fact.Reference;
            // Applied actual facts do not automatically close their independent qualification investigation.
            local.Add(new(r.Owner, r.Id.ToString(), r.Version, r.Digest, fact.Fact.Kind, applied ? "APPLIED_RESTRICTION_UNRESOLVED" : "AWAITING_RECOGNITION_OR_APPLICATION"));
        }
        var unknown = new[] { "WORK_OWNER", "BORROW_OWNER", "STOCK_OWNER", "OA_APPROVAL_OWNER" };
        var items = local.OrderBy(x => x.Owner, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        var version = Hash(JsonSerializer.Serialize(new { toolId = id, tenantId = db.CurrentTenantId, Local = items, UnknownOwners = unknown,
            MaintenanceHead = m.LastOrDefault()?.Reference, ActualHead = a.LastOrDefault()?.Reference }, ToolMaintenanceStoreV1.Json));
        return new(version, items, unknown);
    }
    private async Task<Context> Prepare(OperationsAccessV1 a, Guid id, bool write, CancellationToken ct)
    {
        if (id == Guid.Empty) throw Error("TOOL_INSTANCE_ID_INVALID", 400);
        var query = await a.SourceAsync(OperationsOwnerV1.ToolInstance, ct); var tools = new ToolInstanceStoreV1(db); await tools.Fence(ct, write);
        var native = await tools.Read(query, id, null, ct);
        var registration = await new ToolRegistrationOwnerV1(db, owner).ReadOwnedAsync(query, id, ct) ?? throw Error("LEGACY_MAPPING_REQUIRED", 409);
        var store = new ToolDispositionStoreV1(db);
        return new(a, query, tools, store, native, registration, await store.Facts(id, ct), await Obligations(id, ct));
    }
    public Task<ToolDispositionContextV1> ContextAsync(Guid id, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, async (a, t) =>
        {
            var c = await Prepare(a, id, false, t); var request = c.Facts.SingleOrDefault(x => x.Fact.Kind == "REQUEST"); var block = c.Facts.SingleOrDefault(x => x.Fact.Kind == "BLOCK");
            return new ToolDispositionContextV1(db.CurrentTenantId, id, c.Etag, c.Native.Reference, c.Registration.CurrentSite.Reference,
                block != null ? "BLOCKED_PENDING_DISPOSITION" : request != null ? "REQUESTED" : "NOT_REQUESTED", block != null, request, block, c.Obligations,
                IsDiscarded: c.Native.Source.GetProperty("status").GetInt32() == 3);
        }, ct);
    private static void Text(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw Error("TOOL_DISPOSITION_INPUT_INVALID", 400);
        try { new UTF8Encoding(false, true).GetByteCount(value); } catch (EncoderFallbackException) { throw Error("TOOL_DISPOSITION_INPUT_INVALID", 400); }
    }
    public Task<ToolDispositionResultV1> RequestAsync(Guid id, string? etag, ToolDispositionRequestV1 input, CancellationToken ct = default)
    {
        Text(input.Reason, 500); var cmd = OperationsOwnerV1.Freeze(input);
        if (cmd.ReasonCode is not ("WEAR" or "DAMAGE" or "OBSOLETE")) throw Error("TOOL_DISPOSITION_REASON_INVALID", 400);
        return Write(id, etag, cmd.OperationId, "REQUEST", cmd.ExpectedObligationVersion, cmd, null, cmd.ReasonCode, cmd.Reason, ct);
    }
    public Task<ToolDispositionResultV1> BlockAsync(Guid id, string? etag, ToolDispositionBlockV1 input, CancellationToken ct = default)
    {
        Text(input.Reason, 500); var cmd = OperationsOwnerV1.Freeze(input);
        if (cmd.Request == null) throw Error("TOOL_DISPOSITION_REQUEST_REQUIRED", 400);
        return Write(id, etag, cmd.OperationId, "BLOCK", cmd.ExpectedObligationVersion, cmd, cmd.Request, "", cmd.Reason, ct);
    }
    private Task<ToolDispositionResultV1> Write<T>(Guid id, string? etag, Guid operation, string kind, string obligations, T command,
        ToolDispositionRefV1? request, string reasonCode, string reason, CancellationToken ct)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        var bytes = MainOwnerOperationJournalV1.Encode(new { contract = Contract, id, etag, kind, command });
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "dispose", true, async (a, t) =>
        {
            var c = await Prepare(a, id, true, t);
            if (original != null) return await Disclose(c, operation, original, bytes, t);
            if (string.IsNullOrEmpty(etag)) throw Error("IF_MATCH_REQUIRED", 428);
            if (etag != c.Etag || obligations != c.Obligations.Version) throw Error("TOOL_DISPOSITION_CONTEXT_CHANGED", 412);
            await a.RequireToolInstance(db, id, t);
            if (c.Native.Source.GetProperty("status").GetInt32() == 3) throw Error("TOOL_INSTANCE_DISCARDED", 409);
            if (c.Facts.Any(x => x.Fact.Kind == kind)) throw Error("TOOL_DISPOSITION_ALREADY_RECORDED", 409);
            if (kind == "BLOCK")
            {
                var r = c.Facts.SingleOrDefault(x => x.Fact.Kind == "REQUEST" && x.Reference == request) ?? throw Error("TOOL_DISPOSITION_REQUEST_CHANGED", 412);
                reasonCode = r.Fact.ReasonCode;
            }
            var witnesses = a.Authorities.DistinctBy(x => (x.Resource, x.Action)).Select(x => new ToolMaintenanceAuthorityV1(x.Resource, x.Action, x.Source.SourcePermissionVersion, x.OrganizationVersion)).ToArray();
            var fact = await c.Store.Append(new(db.CurrentTenantId, id, Guid.NewGuid(), kind, a.ActorId, DateTimeOffset.UtcNow,
                c.Native.Reference, c.Registration.CurrentSite.Reference, witnesses, reasonCode, reason, request, c.Obligations), t);
            var result = new ToolDispositionResultV1(operation, id, kind, fact.Reference, kind == "BLOCK" ? "BLOCKED_PENDING_DISPOSITION" : "REQUESTED");
            await journal.Store(Journal, Contract, operation, a.ActorId, bytes, result, [id], new { kind, a.ActorId, fact.Reference, Native = c.Native.Reference, c.Obligations.Version }, t);
            await c.Tools.HoldExactAsync(c.Query, c.Native.Reference, t);
            a.AtCommit(async token =>
            {
                await c.Store.Fence(true, token);
                if ((await c.Store.Facts(id, token)).Last().Reference != fact.Reference || (await Obligations(id, token)).Version != c.Obligations.Version)
                    throw Error("TOOL_DISPOSITION_CONTEXT_CHANGED", 412);
                var sites = await c.Query.SourceAsync(OperationsOwnerV1.PhysicalSite, token);
                if ((await PhysicalSiteIdentityReaderV1.ReadOwnedAsync(db, sites, c.Registration.CurrentSite.Reference.Id, token)).Reference != c.Registration.CurrentSite.Reference)
                    throw Error("TOOL_SITE_CHANGED", 412);
                await a.RequireToolInstance(db, id, token);
                if ((await c.Tools.Read(c.Query, id, null, token)).Reference != c.Native.Reference) throw Error("TOOL_INSTANCE_CHANGED", 412);
            });
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Journal, operation, t); return original == null ? "dispose" : "query"; });
    }
    private async Task<ToolDispositionResultV1> Disclose(Context c, Guid operation, MainOwnerOperationJournalV1.Original original, byte[]? bytes, CancellationToken ct)
    {
        if (original.Actor != c.Access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (bytes != null && !MainOwnerOperationJournalV1.Matches(original, bytes)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ToolDispositionResultV1>(original.Result);
        if (original.State != "COMMITTED" || result == null || result.OperationId != operation || result.ObjectId != c.Native.Reference.Id
            || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId || result.IsDiscarded || result.IsUsePermit
            || !c.Facts.Any(f => f.Reference == result.Fact && f.Fact.Kind == result.Kind)) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var fact = c.Facts.Single(f => f.Reference == result.Fact);
        await c.Tools.Read(c.Query, result.ObjectId, Revision(fact.Fact.Native.Version), ct); return result;
    }
    public Task<ToolDispositionOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", close, async (a, t) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Journal, operation, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new ToolDispositionOperationV1(operation, "CLOSED");
                if (original.Objects.Length != 1) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
                var c = await Prepare(a, original.Objects[0], close, t); return new(operation, "COMMITTED", await Disclose(c, operation, original, null, t));
            }
            if (!close) return new(operation, "UNKNOWN"); var result = new ToolDispositionOperationV1(operation, "CLOSED");
            await journal.Store(Journal, Contract, operation, a.ActorId, null, result, [], new { action = "close-disposition-operation" }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
