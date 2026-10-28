using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.InventoryIdentityValuesV1;

namespace CP6.Core.Services.Wms;

public sealed partial class InventoryIdentityOwnerV1(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 scope,
    QualitySpecificationRuntimeV1 runtime, EngineeringBaselineOwnerV1 baselines)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) && baselines.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private Guid Environment => scope.EnvironmentId;
    private async Task<InventoryIdentityStoreV1> Store(OperationsAccessV1 a, bool write, CancellationToken ct)
    {
        if (runtime.EvidenceMode is not ("REAL" or "DEMO")) throw Error("INVENTORY_IDENTITY_ENVIRONMENT_UNAVAILABLE", 503);
        await scope.Root(ct); var lease = await scope.AcquireAsync(ct); a.AtCommit(t => scope.ValidateAsync(lease, t));
        var s = new InventoryIdentityStoreV1(db, Environment); await s.Fence(write, ct); return s;
    }
    public Task<InventoryIdentityScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.InventoryIdentity, "query", false, async (a, t) =>
        { _ = await Store(a, false, t); return new InventoryIdentityScopeV1(db.CurrentTenantId, a.ActorId, Environment, runtime.EvidenceMode); }, ct);
    public Task<InventoryIdentityCreationV1> CreationContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.InventoryIdentity, "add", false, async (a, t) =>
        { _ = await Store(a, false, t); return await a.InventoryIdentityCreationContext(t); }, ct);
    public Task<InventoryIdentityViewV1[]> ListAsync(string? kind = null, string? search = null, int page = 1, CancellationToken ct = default)
    {
        if (kind != null && kind is not ("PARTY" or "PURPOSE" or "PRODUCT") || search?.Length > 128 || page is < 1 or > 100000)
            throw Error("INVENTORY_IDENTITY_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.InventoryIdentity, "query", false, async (a, t) =>
        {
            var s = await Store(a, false, t); var rows = a.InventoryIdentities(Environment);
            if (kind != null) rows = rows.Where(x => x.Kind == kind);
            if (!string.IsNullOrEmpty(search)) rows = rows.Where(x => x.Code.Contains(search));
            var ids = await rows.OrderBy(x => x.Code).ThenBy(x => x.Id).Skip((page - 1) * 20).Take(20).Select(x => x.Id).ToArrayAsync(t);
            var values = new List<InventoryIdentityViewV1>(); foreach (var id in ids) values.Add(await View(a, s, id, null, t)); return values.ToArray();
        }, ct);
    }
    public Task<InventoryIdentityViewV1> ReadAsync(Guid id, string? version = null, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("INVENTORY_IDENTITY_ID_INVALID", 400); var revision = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.InventoryIdentity, "query", false,
            async (a, t) => await View(a, await Store(a, false, t), id, revision, t), ct);
    }
    private async Task<InventoryIdentityViewV1> Original(OperationsAccessV1 a, InventoryIdentityStoreV1 s, Guid id, long? revision, CancellationToken ct)
    {
        await a.RequireInventoryIdentity(Environment, id, ct); a.AtCommit(t => a.RequireInventoryIdentity(Environment, id, t));
        var v = await s.Read(id, revision, ct);
        if (v.Original.EvidenceMode != runtime.EvidenceMode) throw Error("INVENTORY_IDENTITY_MODE_MISMATCH", 403);
        return v;
    }
    private async Task<InventoryIdentityViewV1> View(OperationsAccessV1 a, InventoryIdentityStoreV1 s, Guid id, long? revision, CancellationToken ct)
    {
        var v = await Original(a, s, id, revision, ct);
        // Historical disclosure still requires today's object/field permissions for every saved original.
        var historical = await Sources(a, s, v.Original.Definition, false, ct);
        if (!SameSources(historical, v.Original.Sources)) throw Error("INVENTORY_IDENTITY_ORIGINAL_UNAVAILABLE", 503);
        var reasons = new List<string>();
        if (!v.IsCurrent) reasons.Add("INVENTORY_IDENTITY_CHANGED");
        if (v.Original.State != "APPROVED") reasons.Add("INVENTORY_IDENTITY_" + v.Original.State);
        if (reasons.Count == 0)
        {
            try { await a.EngineeringObservation(async (observation, t) => { await Selectable(observation, s, v, t); return true; }, ct); }
            catch (OrderPriceCorrectionException e) when (e.Status is 403 or 412 or 422) { reasons.Add(e.Message); }
        }
        return v with { CanSelect = reasons.Count == 0, Reasons = reasons.ToArray() };
    }
    private static bool SameSources(InventoryIdentitySourceV1[] x, InventoryIdentitySourceV1[] y) => x.SequenceEqual(y);
    private static string Action(InventoryIdentityCommandV1 c) => c.Action switch
    { "SAVE" => c.ExpectedVersion == "absent" ? "add" : "edit", "APPROVE" => "approve", "DISABLE" => "disable", _ => throw Error("INVENTORY_IDENTITY_ACTION_INVALID", 400) };
    public Task<InventoryIdentityResultV1> CommandAsync(InventoryIdentityCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.Id == Guid.Empty || input.OperationId == Guid.Empty) throw Error("INVENTORY_IDENTITY_COMMAND_INVALID", 400);
        var c = OperationsOwnerV1.Freeze(input); var action = Action(c); Text(c.Reason, 4000);
        if (c.Action == "SAVE") Definition(c.Definition); else if (c.Definition != null || c.ExpectedVersion == "absent") throw Error("INVENTORY_IDENTITY_COMMAND_INVALID", 400);
        if (c.ExpectedVersion != "absent") { Revision(c.ExpectedVersion); if (c.ExpectedAuthorityVersion != null || c.DepartmentId != null) throw Error("INVENTORY_IDENTITY_COMMAND_INVALID", 400); }
        var bytes = MainOwnerOperationJournalV1.Encode(c); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.InventoryIdentity, action, true, async (a, t) =>
        {
            var s = await Store(a, true, t);
            if (original != null) return await Disclose(a, s, original, c.OperationId, bytes, t);
            InventoryIdentityViewV1? before = null; OperationsAccessV1.NewInventoryIdentityBinding? creation = null;
            if (c.ExpectedVersion == "absent") creation = await a.BeginInventoryIdentityCreation(Environment, c.Id, c.ExpectedAuthorityVersion, c.DepartmentId, t);
            else { before = await Original(a, s, c.Id, null, t); if (before.Reference.Version != c.ExpectedVersion) throw Error("INVENTORY_IDENTITY_CHANGED", 412); }
            var definition = c.Definition ?? before!.Original.Definition;
            var state = c.Action switch { "APPROVE" => "APPROVED", "DISABLE" => "DISABLED", _ => "DRAFT" };
            if (c.Action == "APPROVE" && (before!.Original.State != "DRAFT" || before.Original.AuthorId == a.ActorId))
                throw Error("INVENTORY_IDENTITY_INDEPENDENT_APPROVER_REQUIRED", 403);
            if (c.Action == "DISABLE" && before!.Original.State == "DISABLED") throw Error("INVENTORY_IDENTITY_ALREADY_DISABLED", 409);
            var sources = await Sources(a, s, definition, c.Action != "DISABLE", t);
            var confirmations = c.Action == "APPROVE" ? await Confirmations(a, definition, t) : Array.Empty<EngineeringOwnerConfirmationV1>();
            var saved = await s.Append(c, before, a.ActorId, state, runtime.EvidenceMode, sources, confirmations, t);
            if (creation != null) await a.CompleteInventoryIdentityCreation(Environment, creation, t);
            var result = new InventoryIdentityResultV1(c.OperationId, c.Id, saved.Reference.Version, saved.Reference.Digest, before?.Reference != saved.Reference);
            await journal.Store(Owner, Contract, c.OperationId, a.ActorId, bytes, result, [c.Id], new { c.Action, c.Reason, result }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Owner, c.OperationId, t); return original == null ? action : "query"; });
    }
    private async Task<InventoryIdentityResultV1> Disclose(OperationsAccessV1 a, InventoryIdentityStoreV1 s, MainOwnerOperationJournalV1.Original o,
        Guid operation, byte[]? input, CancellationToken ct)
    {
        if (o.Actor != a.ActorId || o.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (o.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(o, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<InventoryIdentityResultV1>(o.Result);
        if (result == null || result.OperationId != operation || !o.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var v = await View(a, s, result.ObjectId, Revision(result.Version), ct);
        if (v.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<InventoryIdentityOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.InventoryIdentity, "query", close, async (a, t) =>
        {
            var s = await Store(a, close, t); var j = new MainOwnerOperationJournalV1(db); var original = await j.Read(Owner, id, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") : new InventoryIdentityOperationV1(id, "COMMITTED", await Disclose(a, s, original, id, null, t));
            }
            if (!close) return new InventoryIdentityOperationV1(id, "UNKNOWN");
            var result = new InventoryIdentityOperationV1(id, "CLOSED"); await j.Store(Owner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-inventory-identity" }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
    public Task<InventoryProductCatalogueV1> CatalogueAsync(InventoryIdentityRefV1 reference, CancellationToken ct = default)
    {
        Reference(reference);
        return Authority.ExecuteAsync(OperationsOwnerV1.InventoryIdentity, "query", false, async (a, t) =>
        {
            var s = await Store(a, false, t); var v = await Exact(a, s, reference, "PRODUCT", true, t); var p = v.Original.Definition.Product!;
            var party = await Exact(a, s, p.Party, "PARTY", true, t);
            var baseRef = v.Original.Sources.Single(x => x.Path == "baseUom").Reference;
            return new InventoryProductCatalogueV1(v.Reference, new(p.Product, v.Original.Definition.Name,
                new(baseRef.Id.ToString(), baseRef.Version), new(baseRef.Id.ToString(), baseRef.Version),
                new(p.TechnicalKind, p.Baseline, p.TechnicalKind == "NOT_APPLICABLE" ? AsSource(v.Reference) : null),
                new(p.Purpose.Id.ToString(), p.Purpose.Version), new(party.Original.Definition.Party!.Kind,
                    party.Original.Definition.Party.Customer is {} customer ? new(customer.Owner, customer.Id, customer.Version, customer.Digest) : AsSource(p.Party))), v.Original.Sources);
        }, ct);
    }
    private static EngineeringSourceRefV1 AsSource(InventoryIdentityRefV1 r) => new(r.Owner, r.Id, r.Version, r.Digest);
}
