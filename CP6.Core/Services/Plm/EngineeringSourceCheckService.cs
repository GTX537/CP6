using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringSourceCheckValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringSourceCheckService(CP6Context db, OperationsOwnerV1 owner,
    ControlledDocumentOwnerV1 documents, QualitySpecificationAuthorityService quality)
{
    internal bool Owns(CP6Context context)=>ReferenceEquals(db,context) && owner.Owns(db) && documents.Owns(db) && quality.Owns(db);
    internal async Task<EngineeringCheckViewV1> CandidateSource(OperationsAccessV1 root,EngineeringSourceRefV1 reference,CancellationToken ct)
    {
        IterationCandidateValuesV1.Reference(reference,EngineeringSourceCheckValuesV1.Owner);
        if(reference.Version!="1")throw Error("ENGINEERING_CHECK_REFERENCE_INVALID",400);
        var access=await root.SourceAsync(OperationsOwnerV1.EngineeringRevision,ct);
        var store=new EngineeringSourceCheckStoreV1(db);await store.Fence(ct);
        // Compare this provider's authority observation, not the consuming Owner's additional
        // participants. Successful callbacks still join the root's final same-UoW fence.
        var value=await access.EngineeringObservation((observation,t)=>View(observation,store,reference.Id,t),ct);
        if(value.Reference!=reference)throw Error("ENGINEERING_CHECK_REFERENCE_CHANGED",412);
        return value;
    }
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private sealed record Evaluation(EngineeringRevisionSnapshotV1 Revision, ConfigurationRuleSnapshotV1 Policy, Observation Observed, string Authority);
    private async Task<Evaluation> Evaluate(OperationsAccessV1 root, EngineeringSourceCheckCommandV1 command, CancellationToken ct)
    {
        await root.RequireEngineeringRevision(command.Revision.Id, ct); root.AtCommit(t => root.RequireEngineeringRevision(command.Revision.Id, t));
        var revisions = new EngineeringRevisionStoreV1(db); await revisions.Fence(false, ct);
        var revision = await revisions.Read(command.Revision.Id, Revision(command.Revision.Version), ct);
        if (revision.Reference != command.Revision || revision.CandidateRef != command.Candidate || revision.Etag != command.ExpectedEtag)
            throw Error("ENGINEERING_CHECK_INPUT_CHANGED", 412);
        var policy = await new ConfigurationRuleOwnerV1(db, owner).Source(root, command.Policy, revision.Draft.ProductRef, false, ct);
        if (policy.Body.Scope != PolicyScope || policy.Body.EngineeringChecks == null) throw Error("ENGINEERING_CHECK_POLICY_REQUIRED", 422);
        Policy(policy.Body.EngineeringChecks); var rules = policy.Body.EngineeringChecks; var o = new Observation();
        o.Pin("policy", policy.Reference, "cp6_configuration_rule_v1", policy.OriginalJson,
            new { policy.ActiveRef, policy.IsActive, policy.Missing },
            policy.IsActive && policy.State == "APPROVED" && policy.Missing!.Length == 0 ? [] : ["CHECK_POLICY_CHANGED"]);
        if (!revision.IsCurrent) o.Item("candidate", "BLOCK", "CANDIDATE_CHANGED");
        else o.Item("candidate", "PASS", "EXACT_CANDIDATE", source: Ref(revision.CandidateRef));
        EngineeringProductVersionV1? product = null;
        if (revision.Draft.ProductRef is { } productRef)
            await Attempt(root, o, "product", async (a, t) => { product = await Product(a, o, "product", productRef, t); }, ct);
        else o.Item("product", "UNKNOWN", "PRODUCT_REQUIRED");
        var validCategory = product != null && product.Product.Category == rules.ProductCategory && policy.Body.ProductRef == revision.Draft.ProductRef;
        o.Item("policy/productCategory", validCategory ? "PASS" : product == null ? "UNKNOWN" : "BLOCK", validCategory ? "EXACT_PRODUCT_POLICY" : "CHECK_POLICY_PRODUCT_MISMATCH");
        if (revision.Draft.Configuration is not { } configuration) o.Item("configuration", "UNKNOWN", "CONFIGURATION_REQUIRED");
        else
        {
            if (configuration.Input.RuleRef != policy.Reference) o.Item("configuration", "BLOCK", "CONFIGURATION_POLICY_MISMATCH");
            var options = ConfigurationRuleValuesV1.Evaluate(policy, configuration.Input.Options);
            o.Item("configuration/options", options.Status == "OPTIONS_VALIDATED" ? "PASS" : options.Status == "UNKNOWN" ? "UNKNOWN" : "BLOCK", options.Status);
            foreach (var finding in options.Findings) o.Item("configuration/options/" + finding.Name,
                options.Status == "UNKNOWN" ? "UNKNOWN" : "BLOCK", finding.Code, finding.ConstraintId ?? "");
            if (configuration.Input.Applicability != rules.ConfigurationApplicability)
                o.Item("configuration/applicability", configuration.Input.Applicability == "UNKNOWN" ? "UNKNOWN" : "BLOCK", "CONFIGURATION_APPLICABILITY_MISMATCH");
            else if (validCategory && policy.IsActive) o.Item("configuration/applicability", rules.ConfigurationApplicability == "APPLIES" ? "PASS" : "NOT_APPLICABLE", "APPROVED_POLICY", rules.Reason, policy.Reference);
        }
        foreach (var rule in rules.Composition)
        {
            var components = revision.Draft.Components.Where(x => x.Kind == rule.Kind).ToArray();
            var path = "composition/" + rule.Kind;
            if (!validCategory || !policy.IsActive) o.Item(path, "UNKNOWN", "CURRENT_APPLICABILITY_POLICY_REQUIRED");
            else if (rule.Applicability == "NOT_APPLICABLE")
            {
                if (components.Any(x => x.Applicability != "NOT_APPLICABLE")) o.Item(path, "BLOCK", "COMPONENT_APPLICABILITY_BYPASS");
                else o.Item(path, "NOT_APPLICABLE", "APPROVED_CATEGORY_POLICY", rule.Reason, policy.Reference);
            }
            else if (components.Count(x => x.Applicability == "APPLIES") < rule.MinimumOccurrences)
                o.Item(path, "BLOCK", "REQUIRED_OCCURRENCES_MISSING", rule.Reason, policy.Reference);
            else o.Item(path, "PASS", "REQUIRED_OCCURRENCES_PRESENT", rule.Reason, policy.Reference);
        }
        foreach (var component in revision.Draft.Components)
        {
            var path = "components/" + component.OccurrenceId;
            var rule = rules.Composition.SingleOrDefault(x => x.Kind == component.Kind);
            if (component.Applicability == "UNKNOWN") o.Item(path, "UNKNOWN", "COMPONENT_APPLICABILITY_UNKNOWN");
            if (component.Applicability == "NOT_APPLICABLE" && (rule?.Applicability != "NOT_APPLICABLE" || !validCategory || !policy.IsActive))
                o.Item(path, "BLOCK", "COMPONENT_APPLICABILITY_BYPASS");
            if (component.SourceRef == null)
            {
                if (component.Applicability != "NOT_APPLICABLE" || rule?.Applicability != "NOT_APPLICABLE") o.Item(path, "UNKNOWN", "SOURCE_REQUIRED");
                continue;
            }
            await Attempt(root, o, path, (a, t) => Component(a, o, path, component, revision, t), ct);
        }
        return new(revision, policy, o, root.EngineeringAuthorityWatermark());
    }
    private static string Fingerprint(Evaluation value) => Digest(new
    {
        revision = value.Revision.Reference, value.Revision.CandidateRef, value.Revision.IsCurrent,
        policy = value.Policy.Reference, value.Policy.ActiveRef, observation = value.Observed.Fingerprint()
    });
    private void Final(OperationsAccessV1 root, EngineeringSourceCheckCommandV1 command, Evaluation observed)
    {
        var expected = Fingerprint(observed);
        root.AtCommit(async token =>
        {
            var current = await root.EngineeringFinalObservation((a, t) => Evaluate(a, command, t), token);
            if (Fingerprint(current) != expected) throw Error("ENGINEERING_CHECK_SOURCE_CHANGED_AT_COMMIT", 412);
        });
    }
    public Task<EngineeringRevisionScopeV1> ScopeAsync(CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false,
            (a, _) => Task.FromResult(new EngineeringRevisionScopeV1(db.CurrentTenantId, a.ActorId)), ct, readOnlyAuthorityFence: true);
    public Task<EngineeringSourceCheckResultV1> CheckAsync(EngineeringSourceCheckCommandV1 input, CancellationToken ct = default)
    {
        if (input == null || input.OperationId == Guid.Empty || input.Revision == null || input.Candidate == null
            || input.Revision.Owner != EngineeringRevisionValuesV1.RevisionOwner || input.Candidate.Owner != EngineeringRevisionValuesV1.CandidateOwner
            || input.Revision.Id == Guid.Empty || input.Candidate.Id == Guid.Empty || input.ExpectedEtag == Guid.Empty
            || !ValidDigest(input.Revision.Digest) || !ValidDigest(input.Candidate.Digest)) throw Error("ENGINEERING_CHECK_COMMAND_INVALID", 400);
        Revision(input.Revision.Version); Revision(input.Candidate.Version); ConfigurationRuleValuesV1.Ref(input.Policy); EngineeringRevisionValuesV1.Text(input.Reason);
        var frozen = OperationsOwnerV1.Freeze(input); var bytes = MainOwnerOperationJournalV1.Encode(frozen);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "edit", true, async (a, token) =>
        {
            var store = new EngineeringSourceCheckStoreV1(db); await store.Fence(token);
            if (original != null) return await Disclose(a, store, original, frozen.OperationId, bytes, token);
            var evaluated = await Evaluate(a, frozen, token);
            if (!evaluated.Revision.IsCurrent) throw Error("ENGINEERING_CHECK_INPUT_CHANGED", 412);
            Final(a, frozen, evaluated);
            var o = evaluated.Observed;
            var manifest = evaluated.Revision.Draft.ProductRef != null && evaluated.Revision.Draft.Configuration != null
                && evaluated.Revision.Draft.Components.All(x => x.SourceRef != null || x.Applicability == "NOT_APPLICABLE") ? Manifest(evaluated.Revision) : null;
            if (manifest == null) o.Item("manifest", "UNKNOWN", "TECHNICAL_MANIFEST_INPUT_INCOMPLETE");
            var value = new EngineeringCheckRunV1(db.CurrentTenantId, Guid.NewGuid(), a.ActorId, DateTimeOffset.UtcNow, frozen,
                evaluated.Revision.Draft, evaluated.Policy.Body, Digest(evaluated.Revision.Draft), manifest,
                o.Items.ToArray(), o.Sources.ToArray(), State(o.Items), evaluated.Authority);
            var reference = await store.Save(value, token);
            var result = new EngineeringSourceCheckResultV1(frozen.OperationId, value.Id, frozen.Revision.Id, reference.Digest);
            await journal.Store(EngineeringSourceCheckValuesV1.Owner, Contract, frozen.OperationId, a.ActorId, bytes, result, [frozen.Revision.Id],
                new { action = "engineering-source-check", actor = a.ActorId, frozen.Reason, result, candidate = frozen.Candidate, policy = frozen.Policy, value.State }, token);
            return result;
        }, ct, actionAtFence: async token => { original = await journal.Read(EngineeringSourceCheckValuesV1.Owner, frozen.OperationId, token); return original == null ? "edit" : "query"; });
    }
    private async Task<EngineeringCheckViewV1> View(OperationsAccessV1 a, EngineeringSourceCheckStoreV1 store, Guid id, CancellationToken ct)
    {
        var (reference, saved, json) = await store.Read(id, ct); await a.RequireEngineeringRevision(saved.Command.Revision.Id, ct);
        var current = await Evaluate(a, saved.Command, ct);
        if (current.Observed.Items.Any(x => x.Code == "SOURCE_ACCESS_DENIED")) throw Error("OBJECT_FORBIDDEN", 403);
        var changes = new List<string>();
        if (!current.Revision.IsCurrent) changes.Add("CANDIDATE_CHANGED");
        if (current.Policy.ActiveRef != saved.Command.Policy) changes.Add("CHECK_POLICY_CHANGED");
        if (Digest(current.Observed.Sources) != Digest(saved.Sources)) changes.Add("SOURCE_WATERMARK_CHANGED");
        if (saved.ActorId == a.ActorId && current.Authority != saved.AuthorityWatermark) changes.Add("AUTHORITY_CHANGED");
        Final(a, saved.Command, current);
        return new(reference, saved, json, changes.Count == 0 ? "CURRENT" : "STALE", changes.ToArray());
    }
    public Task<EngineeringCheckViewV1> ReadAsync(Guid id, CancellationToken ct = default)
        => Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (a, t) =>
        { var store = new EngineeringSourceCheckStoreV1(db); await store.Fence(t); return await View(a, store, id, t); }, ct);
    public Task<EngineeringCheckViewV1[]> HistoryAsync(Guid revision, int page = 1, CancellationToken ct = default)
    {
        if (revision == Guid.Empty || page is < 1 or > 100000) throw Error("ENGINEERING_CHECK_QUERY_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (a, t) =>
        {
            await a.RequireEngineeringRevision(revision, t); var store = new EngineeringSourceCheckStoreV1(db); await store.Fence(t);
            var result = new List<EngineeringCheckViewV1>(); foreach (var id in await store.History(revision, page, t)) result.Add(await View(a, store, id, t)); return result.ToArray();
        }, ct);
    }
    // Internal participant only. A frozen receipt/HTTP observation is never a reusable final-use token.
    internal async Task<EngineeringCheckRunV1> HoldExact(OperationsAccessV1 root, EngineeringSourceRefV1 reference, CancellationToken ct)
    {
        if (reference.Owner != EngineeringSourceCheckValuesV1.Owner || reference.Version != "1") throw Error("ENGINEERING_CHECK_REFERENCE_INVALID", 400);
        var access = await root.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        var store = new EngineeringSourceCheckStoreV1(db); await store.Fence(ct); var (actual, saved, _) = await store.Read(reference.Id, ct);
        if (actual != reference) throw Error("ENGINEERING_CHECK_REFERENCE_CHANGED", 412);
        var current = await Evaluate(access, saved.Command, ct);
        if (saved.State != "PASS" || State(current.Observed.Items) != "PASS" || !current.Revision.IsCurrent
            || current.Policy.ActiveRef != saved.Command.Policy || Digest(current.Observed.Sources) != Digest(saved.Sources))
            throw Error("ENGINEERING_CHECK_NOT_CURRENT_PASS", 412);
        Final(access, saved.Command, current); return saved;
    }
    private async Task<EngineeringSourceCheckResultV1> Disclose(OperationsAccessV1 a, EngineeringSourceCheckStoreV1 store,
        MainOwnerOperationJournalV1.Original original, Guid id, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<EngineeringSourceCheckResultV1>(original.Result);
        if (result == null || result.OperationId != id || !original.Objects.SequenceEqual(new[] { result.RevisionId })) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        var view = await View(a, store, result.CheckRunId, ct);
        if (view.Reference.Digest != result.Digest) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503); return result;
    }
    public Task<EngineeringSourceCheckOperationV1> OperationAsync(Guid id, bool close, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", close, async (a, t) =>
        {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(EngineeringSourceCheckValuesV1.Owner, id, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new EngineeringSourceCheckOperationV1(id, "CLOSED");
                var store = new EngineeringSourceCheckStoreV1(db); await store.Fence(t);
                return new EngineeringSourceCheckOperationV1(id, "COMMITTED", await Disclose(a, store, original, id, null, t));
            }
            if (!close) return new EngineeringSourceCheckOperationV1(id, "UNKNOWN");
            var result = new EngineeringSourceCheckOperationV1(id, "CLOSED");
            await journal.Store(EngineeringSourceCheckValuesV1.Owner, Contract, id, a.ActorId, null, result, [], new { action = "close-uncommitted-engineering-check", actor = a.ActorId }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
