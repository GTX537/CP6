using System.Text.Json;
using CP6.Core.Services.Integration;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;
using static CP6.Core.Services.Plm.EngineeringRevisionValuesV1;

namespace CP6.Core.Services.Plm;

public sealed partial class EngineeringRevisionOwnerV1
{
    public Task<EngineeringRevisionConflictV1> ConflictAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.EngineeringRevision, "query", false, async (access, token) =>
        {
            var store = new EngineeringRevisionStoreV1(db);
            await store.Fence(false, token);
            var original = await new EngineeringRevisionConflictStoreV1(db).Read(id, token)
                ?? throw Error("ENGINEERING_REVISION_CONFLICT_NOT_FOUND", 404);
            return await DiscloseConflict(access, store, original, id, token);
        }, ct, readOnlyAuthorityFence: true);
    }

    private async Task<EngineeringRevisionConflictV1> CaptureConflict(OperationsAccessV1 access, EngineeringRevisionStoreV1 store,
        EngineeringRevisionCommandV1 command, EngineeringRevisionSnapshotV1 expected, EngineeringRevisionSnapshotV1 current, CancellationToken ct)
    {
        var attempted = await ObserveRejectedDraft(access, store, expected, command, ct);
        var (_, changes) = EngineeringRevisionRebaseV1.Compare(expected.Draft, current.Draft, attempted, compareConfigurationInputs: true);
        var resolutions = command.Rebase?.Resolutions.ToDictionary(x => x.Path, x => x.Reason);
        var expectedMembers = expected.Draft.Components.ToDictionary(x => x.OccurrenceId);
        var currentMembers = current.Draft.Components.ToDictionary(x => x.OccurrenceId);
        var attemptedMembers = attempted.Components.ToDictionary(x => x.OccurrenceId);
        var items = new List<EngineeringRevisionConflictItemV1>(changes.Length);
        foreach (var change in changes)
        {
            Guid? occurrence = null;
            var reason = resolutions?.GetValueOrDefault(change.Path);
            EngineeringSourceRefV1? before, winner, proposed;
            if (change.Path.StartsWith("components/", StringComparison.Ordinal))
            {
                var id = Guid.Parse(change.Path.Split('/')[1]);
                occurrence = id;
                before = expectedMembers.GetValueOrDefault(id)?.SourceRef;
                winner = currentMembers.GetValueOrDefault(id)?.SourceRef;
                proposed = attemptedMembers.GetValueOrDefault(id)?.SourceRef;
                reason ??= resolutions?.GetValueOrDefault($"components/{id}");
            }
            else
            {
                before = DraftSource(change.Path, expected.Draft);
                winner = DraftSource(change.Path, current.Draft);
                proposed = DraftSource(change.Path, attempted);
            }
            items.Add(new(change.Path, occurrence, change.BaseValue, change.CurrentValue, change.IncomingValue,
                before, winner, proposed, reason ?? "工作版本或 ETag 已变化", change.Conflict));
        }
        return new(command.OperationId, command.Action, command.Reason, DateTimeOffset.UtcNow, command.ExpectedEtag!.Value,
            ConflictHead(expected), ConflictHead(current), command.Rebase?.Sources, items.ToArray());
    }

    private async Task<EngineeringRevisionConflictV1> DiscloseConflict(OperationsAccessV1 access, EngineeringRevisionStoreV1 store,
        EngineeringRevisionConflictStoreV1.Original original, Guid id, CancellationToken ct)
    {
        if (original.Actor != access.ActorId) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        var value = original.Decode();
        var command = JsonSerializer.Deserialize<EngineeringRevisionCommandV1>(original.Input);
        if (command == null || value.OperationId != id || command.OperationId != id || command.Action != value.Action
            || command.Action is not ("PATCH" or "REBASE") || command.RevisionId != value.Expected.Revision.Id
            || command.RevisionId != value.Current.Revision.Id || command.ExpectedVersion != value.Expected.Revision.Version
            || command.ExpectedEtag != value.RequestedEtag || command.Reason != value.Reason || command.Rebase?.Sources != value.Sources)
            throw Error("ENGINEERING_REVISION_CONFLICT_ORIGINAL_UNAVAILABLE", 503);

        await access.RequireEngineeringRevision(command.RevisionId, ct);
        var expected = await Observe(access, await store.Read(command.RevisionId, Revision(value.Expected.Revision.Version), ct), ct);
        var current = await Observe(access, await store.Read(command.RevisionId, Revision(value.Current.Revision.Version), ct), ct);
        if (ConflictHead(expected) != value.Expected || ConflictHead(current) != value.Current)
            throw Error("ENGINEERING_REVISION_CONFLICT_ORIGINAL_UNAVAILABLE", 503);
        await ObserveRejectedDraft(access, store, expected, command, ct);
        return value;
    }

    private async Task<EngineeringWorkingDraftV1> ObserveRejectedDraft(OperationsAccessV1 access, EngineeringRevisionStoreV1 store,
        EngineeringRevisionSnapshotV1 expected, EngineeringRevisionCommandV1 command, CancellationToken ct)
    {
        var draft = command.Action == "REBASE"
            ? (await Rebase(access, store, expected, command, ct, historical: true)).Draft
            : Patch(expected.Draft, command.Patches);
        await Observe(access, expected with { Draft = draft }, ct);
        return draft;
    }

    private static EngineeringRevisionConflictHeadV1 ConflictHead(EngineeringRevisionSnapshotV1 value)
        => new(value.Reference, value.CandidateRef, value.Etag);

    private static EngineeringSourceRefV1? DraftSource(string path, EngineeringWorkingDraftV1 draft) => path switch
    {
        "productRef" when draft.ProductRef is { } product => new(product.Owner, product.Id, product.Version, product.Digest),
        "configuration" => draft.Configuration?.Input.RuleRef,
        _ => null
    };
}
