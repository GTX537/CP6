using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Pub;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;
public sealed class EngineeringRevisionOwnerV1(CP6Context db, OperationsOwnerV1 owner, ControlledDocumentOwnerV1? documents = null)
{
    private const string Contract = "Main.Plm.EngineeringRevision.commands.v1";
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public Task<EngineeringRevisionScopeV1> ScopeAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.EngineeringRevision, "query", false, (access, _) => Task.FromResult(new EngineeringRevisionScopeV1(db.CurrentTenantId, access.ActorId)), ct, readOnlyAuthorityFence: true);
    public Task<EngineeringRevisionCreationContextV1> CreationContextAsync(CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.EngineeringRevision, "add", false, async (access, token) =>
        { await new EngineeringRevisionStoreV1(db).Fence(false, token); return await access.EngineeringRevisionCreationContext(token); }, ct, readOnlyAuthorityFence: true);
    private async Task<EngineeringProductVersionV1> Product(OperationsAccessV1 root, EngineeringProductRefV1 reference, bool requireCurrent, CancellationToken ct)
    {
        Reference(reference); var access = await root.SourceAsync(OperationsOwnerV1.EngineeringProduct, ct);
        await access.RequireEngineeringProduct(reference.Id, ct); root.AtCommit(token => access.RequireEngineeringProduct(reference.Id, token));
        var store = new EngineeringProductStoreV1(db); await store.Fence(false, ct);
        var value = await store.Read(reference.Id, Revision(reference.Version), ct);
        if (value.Reference != reference) throw Error("ENGINEERING_REFERENCE_CHANGED", 412);
        if (requireCurrent && (!value.IsCurrent || value.Product.Enabled != true)) throw Error("ENGINEERING_PRODUCT_CHANGED", 412);
        return value;
    }
    private async Task<EngineeringRevisionSnapshotV1> Observe(OperationsAccessV1 access, EngineeringRevisionSnapshotV1 value, CancellationToken ct)
    {
        await access.RequireEngineeringRevision(value.Reference.Id, ct); access.AtCommit(token => access.RequireEngineeringRevision(value.Reference.Id, token));
        var missing = new List<string>();
        if (value.Draft.ProductRef is { } product)
        { var actual = await Product(access, product, false, ct); if (!actual.IsCurrent || actual.Product.Enabled != true) missing.Add("PRODUCT_SOURCE_CHANGED"); }
        else missing.Add("PRODUCT_UNRESOLVED");
        if (value.Draft.Configuration?.Input.RuleRef is { } ruleRef)
        {
            var rule = await new ConfigurationRuleOwnerV1(db, owner).Source(access, ruleRef, value.Draft.ProductRef, false, ct);
            if (!rule.IsActive || rule.State != "APPROVED" || rule.Missing!.Length != 0 || rule.Body.ProductRef != value.Draft.ProductRef) missing.Add("CONFIGURATION_RULE_CHANGED");
            if (value.Draft.Configuration.OptionCheck?.Status != "OPTIONS_VALIDATED") missing.Add("CONFIGURATION_OPTIONS_UNRESOLVED");
            missing.Add("CONFIGURATION_APPLICABILITY_REQUIRED");
        }
        else missing.Add(value.Draft.Configuration == null ? "CONFIGURATION_MISSING" : "CONFIGURATION_RULE_OWNER_REQUIRED");
        if (value.Draft.Components.Length == 0) missing.Add("COMPOSITION_MISSING");
        foreach (var component in value.Draft.Components)
        {
            Component(component);
            if (component.SourceRef is { } source)
            {
                if (component.Kind == "DOCUMENT")
                {
                    var document = await (documents ?? throw Error("DOCUMENT_OWNER_UNAVAILABLE",503)).Source(access,source,value.Reference.Id,value.Draft.ProductRef,false,ct);
                    if (!document.IsCurrent) missing.Add("SOURCE_CHANGED:" + component.OccurrenceId);
                    missing.AddRange(document.Missing!.Select(x => "DOCUMENT:" + component.OccurrenceId + ":" + x));
                    missing.Add("DOCUMENT_APPROVAL_REQUIRED:" + component.OccurrenceId);
                }
                else if (component.Kind == "ROUTING")
                {
                    var route = await new EngineeringRoutingOwnerV1(db, owner).Source(access, source, value.Reference.Id, value.Draft.ProductRef, false, ct);
                    if (!route.IsCurrent) missing.Add("SOURCE_CHANGED:" + component.OccurrenceId);
                    missing.AddRange(route.Missing!.Select(x => "ROUTING:" + component.OccurrenceId + ":" + x));
                    missing.Add("ROUTING_REVIEW_AND_FREEZE_REQUIRED:" + component.OccurrenceId);
                }
                else if (component.Kind is "EBOM" or "MBOM")
                {
                    var bom = await new EngineeringBomOwnerV1(db, owner).Source(access, source, value.Reference.Id, value.Draft.ProductRef, component.Kind, false, ct);
                    if (!bom.IsCurrent) missing.Add("SOURCE_CHANGED:" + component.OccurrenceId);
                    missing.AddRange(bom.Missing!.Select(x => "BOM:" + component.OccurrenceId + ":" + x));
                    missing.Add("BOM_REVIEW_AND_FREEZE_REQUIRED:" + component.OccurrenceId);
                }
                else
                {
                    var actual = await Product(access, new(source.Owner, source.Id, source.Version, source.Digest), false, ct);
                    if (!actual.IsCurrent || actual.Product.Enabled != true) missing.Add("SOURCE_CHANGED:" + component.OccurrenceId);
                }
            }
            else missing.Add("SOURCE_REQUIRED:" + component.OccurrenceId);
            if (component.Applicability != "APPLIES") missing.Add("APPLICABILITY_POLICY_REQUIRED:" + component.OccurrenceId);
        }
        return value with { Missing = missing.ToArray() };
    }
    public Task<EngineeringRevisionSnapshotV1[]> ListAsync(string? search, int page = 1, CancellationToken ct = default)
    {
        if (search?.Length > 256 || page < 1 || page > int.MaxValue / 20) throw Error("ENGINEERING_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(false, token);
            var rows = access.EngineeringRevisions(); if (!string.IsNullOrEmpty(search)) rows = rows.Where(row => row.Code.Contains(search));
            var ids = await rows.OrderBy(row => row.Code).ThenBy(row => row.Id).Skip((page - 1) * 20).Take(20).Select(row => row.Id).ToArrayAsync(token);
            var result = new List<EngineeringRevisionSnapshotV1>(); foreach (var id in ids) result.Add(await Observe(access, await store.Read(id, null, token), token));
            return result.ToArray();
        }, ct);
    }
    public Task<EngineeringRevisionSnapshotV1> ReadAsync(Guid id, string? version, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ENGINEERING_REVISION_INVALID", 400); var selected = version == null ? (long?)null : Revision(version);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(false, token); await access.RequireEngineeringRevision(id, token);
            return await Observe(access, await store.Read(id, selected, token), token);
        }, ct);
    }
    public Task<EngineeringWorkingDiffV1> DiffAsync(Guid id, string before, string after, CancellationToken ct = default)
    {
        var first = Revision(before); var last = Revision(after);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(false, token); await access.RequireEngineeringRevision(id, token);
            var a = await Observe(access, await store.Read(id, first, token), token); var b = await Observe(access, await store.Read(id, last, token), token);
            var changes = new List<EngineeringWorkingChangeV1>();
            void Compare(string path, object? left, object? right)
            {
                JsonElement? x = left == null ? null : JsonSerializer.SerializeToElement(left, EngineeringProductStoreV1.Json);
                JsonElement? y = right == null ? null : JsonSerializer.SerializeToElement(right, EngineeringProductStoreV1.Json);
                if (x?.GetRawText() != y?.GetRawText()) changes.Add(new(path, x, y));
            }
            Compare("goal", a.Draft.Goal, b.Draft.Goal); Compare("productRef", a.Draft.ProductRef, b.Draft.ProductRef);
            Compare("configuration", a.Draft.Configuration, b.Draft.Configuration);
            foreach (var occurrence in a.Draft.Components.Select(row => row.OccurrenceId).Union(b.Draft.Components.Select(row => row.OccurrenceId)).Order())
                Compare("components/" + occurrence, a.Draft.Components.SingleOrDefault(row => row.OccurrenceId == occurrence), b.Draft.Components.SingleOrDefault(row => row.OccurrenceId == occurrence));
            return new EngineeringWorkingDiffV1(a.Reference, b.Reference, changes.ToArray());
        }, ct);
    }
    private async Task<(string Code, EngineeringNumberAllocationV1 Allocation)> Allocate(CancellationToken ct)
    {
        var instant = DateTimeOffset.Now; string code;
        try { code = await new SeqService(db).NextAsync(NumberingKey, instant.DateTime); }
        catch (InvalidOperationException error) when (error.Message is "E-PUB-051" or "RULE_INVALID" or "SEQUENCE_EXHAUSTED")
        { throw Error(error.Message == "E-PUB-051" ? "ENGINEERING_NUMBER_RULE_REQUIRED" : "ENGINEERING_NUMBER_RULE_INVALID"); }
        if (!ValidText(code, 256)) throw Error("ENGINEERING_NUMBER_RULE_INVALID");
        var rule = await db.Pub_DocSequences.AsNoTracking().SingleAsync(row => row.BizKey == NumberingKey, ct);
        return (code, new(rule.Id, rule.BizKey, rule.Prefix, rule.DateFormat, rule.SeqLength, rule.ResetCycle, rule.CurrentPeriod, rule.CurrentValue, instant));
    }
    public Task<EngineeringRevisionResultV1> CommandAsync(EngineeringRevisionCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.RevisionId == Guid.Empty || input.Action is not ("CREATE" or "PATCH")) throw Error("ENGINEERING_COMMAND_INVALID", 400);
        var frozen = OperationsOwnerV1.Freeze(input); Text(frozen.Reason);
        var bytes = MainOwnerOperationJournalV1.Encode(frozen); var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        var action = frozen.Action == "CREATE" ? "add" : "edit";
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, action, true, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(true, token);
            if (original != null) return await Disclose(access, store, original, frozen.OperationId, bytes, token);
            EngineeringWorkingDraftV1 draft; long? expected = null; string? code = null; EngineeringNumberAllocationV1? numbering = null;
            OperationsAccessV1.NewEngineeringRevisionBinding? creation = null;
            if (frozen.Action == "CREATE")
            {
                if (frozen.ExpectedVersion != null || frozen.ExpectedEtag != null || frozen.Patches != null) throw Error("ENGINEERING_COMMAND_INVALID", 400);
                Origin(frozen.Origin); Text(frozen.Goal);
                creation = await access.BeginEngineeringRevisionCreation(frozen.RevisionId, frozen.ExpectedAuthorityVersion, frozen.DepartmentId, token);
                if (frozen.ProductRef != null) await Product(access, frozen.ProductRef, true, token);
                draft = new(frozen.ProductRef, frozen.Goal!, null, []); (code, numbering) = await Allocate(token);
            }
            else
            {
                if (frozen.Origin != null || frozen.ProductRef != null || frozen.Goal != null || frozen.ExpectedAuthorityVersion != null || frozen.DepartmentId != null || frozen.ExpectedEtag == null)
                    throw Error("ENGINEERING_COMMAND_INVALID", 400);
                expected = Revision(frozen.ExpectedVersion); await access.RequireEngineeringRevision(frozen.RevisionId, token);
                var current = await Observe(access, await store.Read(frozen.RevisionId, null, token), token);
                if (current.Reference.Version != frozen.ExpectedVersion || current.Etag != frozen.ExpectedEtag) throw Error("ENGINEERING_WORKING_VERSION_CHANGED", 412);
                if (current.State != "DRAFT") throw Error("ENGINEERING_WORKING_NOT_DRAFT", 409);
                draft = Patch(current.Draft, frozen.Patches);
                foreach (var patch in frozen.Patches!)
                {
                    if (patch.Op == "SET_PRODUCT") await Product(access, patch.ProductRef!, true, token);
                    if (patch.Op == "UPSERT_COMPONENT" && patch.Component!.SourceRef is { } source)
                    {
                        if (patch.Component.Kind == "DOCUMENT")
                            await (documents ?? throw Error("DOCUMENT_OWNER_UNAVAILABLE",503)).Source(access,source,frozen.RevisionId,draft.ProductRef,true,token);
                        else if (patch.Component.Kind == "ROUTING")
                            await new EngineeringRoutingOwnerV1(db, owner).Source(access, source, frozen.RevisionId, draft.ProductRef, true, token);
                        else if (patch.Component.Kind is "EBOM" or "MBOM")
                            await new EngineeringBomOwnerV1(db, owner).Source(access, source, frozen.RevisionId, draft.ProductRef, patch.Component.Kind, true, token);
                        else await Product(access, new(source.Owner, source.Id, source.Version, source.Digest), true, token);
                    }
                }
            }
            if (frozen.Patches?.Any(patch => patch.Op == "SET_CONFIGURATION") == true && draft.Configuration?.Input.RuleRef is { } reference)
            {
                var rule = await new ConfigurationRuleOwnerV1(db, owner).Source(access, reference, draft.ProductRef, true, token);
                draft = draft with { Configuration = draft.Configuration with { OptionCheck = ConfigurationRuleValuesV1.Evaluate(rule, draft.Configuration.Input.Options) } };
            }
            var saved = await store.Append(frozen.RevisionId, access.ActorId, draft, frozen.Reason, expected, frozen.ExpectedEtag, code, frozen.Origin, numbering, token);
            if (creation != null) await access.CompleteEngineeringRevisionCreation(creation, token);
            await Observe(access, saved, token);
            var result = new EngineeringRevisionResultV1(frozen.OperationId, "ENGINEERING_REVISION", frozen.RevisionId, saved.Reference.Version, saved.Reference.Digest);
            await journal.Store(RevisionOwner, Contract, frozen.OperationId, access.ActorId, bytes, result, [frozen.RevisionId],
                new { action = frozen.Action, actor = access.ActorId, reason = frozen.Reason, result, saved.CandidateRef }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(RevisionOwner, frozen.OperationId, token); return original == null ? action : "query"; });
    }
    private async Task<EngineeringRevisionResultV1> Disclose(OperationsAccessV1 access, EngineeringRevisionStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<EngineeringRevisionResultV1>(original.Result);
        if (result == null || result.OperationId != id || result.Kind != "ENGINEERING_REVISION" || !original.Objects.SequenceEqual(new[] { result.ObjectId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var saved = await Observe(access, await store.Read(result.ObjectId, Revision(result.Version), ct), ct);
        if (saved.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<EngineeringRevisionOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", close, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db); await store.Fence(close, token); var journal = new MainOwnerOperationJournalV1(db);
            var original = await journal.Read(RevisionOwner, id, token);
            if (original != null)
            {
                if (original.Actor != access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(id, "CLOSED") : new EngineeringRevisionOperationV1(id, "COMMITTED", await Disclose(access, store, original, id, null, token));
            }
            if (!close) return new EngineeringRevisionOperationV1(id, "UNKNOWN");
            var result = new EngineeringRevisionOperationV1(id, "CLOSED");
            await journal.Store(RevisionOwner, Contract, id, access.ActorId, null, result, [], new { action = "close-uncommitted-engineering-revision", actor = access.ActorId }, token);
            return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
