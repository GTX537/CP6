using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRevisionOwnerV1
{
    public Task<EngineeringRebasePreviewV1> RebasePreviewAsync(Guid id, EngineeringRebaseSourcesV1 input, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ENGINEERING_REVISION_INVALID", 400);
        EngineeringRevisionRebaseV1.References(input);
        var sources = OperationsOwnerV1.Freeze(input);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db);
            await store.Fence(false, token);
            await access.RequireEngineeringRevision(id, token);
            var current = await Observe(access, await store.Read(id, null, token), token);
            var (basis, incoming) = await RebaseSources(access, store, current, sources, token);
            var (_, changes) = EngineeringRevisionRebaseV1.Compare(basis.Draft, current.Draft, incoming.Draft);
            return new EngineeringRebasePreviewV1(current.Reference, current.CandidateRef, current.Etag, sources, changes, basis.Missing!, incoming.Missing!);
        }, ct);
    }

    private async Task<EngineeringRevisionSnapshotV1> RebaseSource(OperationsAccessV1 access, EngineeringRevisionStoreV1 store,
        EngineeringRevisionRefV1 reference, bool requireCurrent, CancellationToken ct)
    {
        if (reference.Owner == EngineeringBaselineValuesV1.Owner)
        {
            var baseline = await (baselines ?? throw Error("ENGINEERING_BASELINE_OWNER_REQUIRED", 503))
                .Source(access, SourceRef(reference), false, ct);
            return baseline.Original.Revision with { Missing = baseline.Changes };
        }

        var query = await access.SourceAsync(OperationsOwnerV1.EngineeringRevision, ct);
        var id = await query.EngineeringRevisions().Where(x => x.CandidateId == reference.Id).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct)
            ?? throw Error("OBJECT_FORBIDDEN", 403);
        var version = await store.Read(id, Revision(reference.Version), ct);
        if (version.CandidateRef != reference || requireCurrent && !version.IsCurrent) throw Error("ENGINEERING_REBASE_SOURCE_CHANGED", 412);
        return await Observe(query, version, ct);
    }

    private static EngineeringSourceRefV1 SourceRef(EngineeringRevisionRefV1 value) => new(value.Owner, value.Id, value.Version, value.Digest);
    private static EngineeringRevisionRefV1 RevisionRef(EngineeringSourceRefV1 value) => new(value.Owner, value.Id, value.Version, value.Digest);

    private async Task<EngineeringRevisionRefV1?> CompositionBase(EngineeringRevisionSnapshotV1 current,
        (long Version, EngineeringRebaseRecordV1 Record)? rebase, CancellationToken ct)
    {
        var version = Revision(current.Reference.Version);
        var successor = baselines == null ? null : await baselines.ParentOfWorkingVersion(current.Reference.Id, version, ct);
        if (successor != null && (rebase == null || successor.Value.Version > rebase.Value.Version)) return RevisionRef(successor.Value.Parent);
        if (rebase != null) return rebase.Value.Record.Input.Sources.Incoming;
        return current.Origin.ParentBaselineRef is { } parent ? RevisionRef(parent) : null;
    }

    private async Task<(EngineeringRevisionSnapshotV1 Base, EngineeringRevisionSnapshotV1 Incoming)> RebaseSources(OperationsAccessV1 access,
        EngineeringRevisionStoreV1 store, EngineeringRevisionSnapshotV1 current, EngineeringRebaseSourcesV1 sources, CancellationToken ct)
    {
        if (current.State != "DRAFT") throw Error("ENGINEERING_WORKING_NOT_DRAFT", 409);
        var previous = await new EngineeringRevisionLineageStoreV1(db).RebaseAtOrBefore(current.Reference.Id, Revision(current.Reference.Version), ct);
        var expectedBase = await CompositionBase(current, previous, ct);
        if (expectedBase != null && sources.Base != expectedBase) throw Error("ENGINEERING_REBASE_BASE_CHANGED", 412);
        if (sources.Incoming == current.CandidateRef) throw Error("ENGINEERING_REBASE_SOURCES_INVALID", 400);
        var basis = await RebaseSource(access, store, sources.Base, false, ct);
        if (expectedBase == null && (basis.Reference.Id != current.Reference.Id || Revision(basis.Reference.Version) >= Revision(current.Reference.Version)))
            throw Error("ENGINEERING_REBASE_BASE_CHANGED", 412);
        var incoming = await RebaseSource(access, store, sources.Incoming, true, ct);
        var product = current.Draft.ProductRef?.Id;
        if (product == null || basis.Draft.ProductRef?.Id != product || incoming.Draft.ProductRef?.Id != product)
            throw Error("ENGINEERING_REBASE_PRODUCT_MISMATCH", 422);
        return (basis, incoming);
    }

    private async Task<EngineeringRevisionSnapshotV1> ObserveLineage(OperationsAccessV1 access, EngineeringRevisionStoreV1 store,
        EngineeringRevisionSnapshotV1 snapshot, CancellationToken ct)
    {
        var version = Revision(snapshot.Reference.Version);
        var saved = await new EngineeringRevisionLineageStoreV1(db).RebaseAtOrBefore(snapshot.Reference.Id, version, ct);
        var basis = await CompositionBase(snapshot, saved, ct);
        var record = saved?.Version == version ? saved.Value.Record : null;
        if (basis != null) await RebaseSource(access, store, basis, false, ct);
        if (record != null && record.Input.Sources.Base != basis) await RebaseSource(access, store, record.Input.Sources.Base, false, ct);
        return snapshot with { CompositionBase = basis, Rebase = record };
    }

    private async Task<(EngineeringWorkingDraftV1 Draft, EngineeringRebaseRecordV1 Record)> Rebase(OperationsAccessV1 access,
        EngineeringRevisionStoreV1 store, EngineeringRevisionSnapshotV1 current, EngineeringRevisionCommandV1 command, CancellationToken ct)
    {
        var input = command.Rebase!;
        var (basis, incoming) = await RebaseSources(access, store, current, input.Sources, ct);
        if (incoming.Origin.Type == "CHANGE") await VerifyOrigin(access, incoming.Origin, incoming.Draft.ProductRef, true, ct);
        var (draft, _) = EngineeringRevisionRebaseV1.Compare(basis.Draft, current.Draft, incoming.Draft, input.Resolutions);
        if (draft.Components.Length > 1000) throw Error("ENGINEERING_COMPONENT_LIMIT", 400);
        if (draft.ProductRef?.Id != current.Draft.ProductRef?.Id) throw Error("ENGINEERING_REVISION_PRODUCT_IMMUTABLE", 409);
        Text(draft.Goal);
        foreach (var member in draft.Components) Component(member);
        if (input.Resolutions.Any(r => r.Path == "configuration" && r.Decision == "MANUAL") && draft.Configuration?.Input.RuleRef is { } ruleRef)
        {
            var rule = await new ConfigurationRuleOwnerV1(db, owner).Source(access, ruleRef, draft.ProductRef, true, ct);
            draft = draft with { Configuration = draft.Configuration with { OptionCheck = ConfigurationRuleValuesV1.Evaluate(rule, draft.Configuration.Input.Options) } };
        }
        return (draft, new(command.OperationId, current.CandidateRef, input, current.Origin.ChangeRef, incoming.Origin.ChangeRef, command.Reason));
    }
}
