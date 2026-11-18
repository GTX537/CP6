using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Plm;
using static CP6.Core.Services.Plm.EngineeringProductValuesV1;

namespace CP6.Core.Services.Pub;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FileDraftCommandV1(Guid OperationId, Guid DraftId, string? ExpectedDraftSetVersion,
    FileContentRefV1[] ContentRefs, string Reason);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FileDraftBindV1(Guid OperationId, Guid DraftId, string ExpectedDraftSetVersion,
    EngineeringRevisionRefV1 Target, FileContentRefV1[] ContentRefs, string Reason);

public sealed record FileDraftResultV1(Guid OperationId, Guid? DraftId, string State, string? DraftSetVersion,
    EngineeringRevisionRefV1? Target, FileContentRefV1[] ContentRefs);

public sealed partial class FileContentOwnerV1
{
    private const string DraftContract = "Main.File.Content.draft-commands.v1";

    public Task<FileDraftResultV1> SaveDraftAsync(FileDraftCommandV1 input, CancellationToken ct = default)
    {
        ValidateDraftCommand(input.OperationId, input.DraftId, input.ContentRefs, input.Reason);
        var expected = input.ExpectedDraftSetVersion is null ? (long?)null : Revision(input.ExpectedDraftSetVersion);
        var command = OperationsOwnerV1.Freeze(input);
        return WriteDraft(command.OperationId, command.DraftId, MainOwnerOperationJournalV1.Encode(command), command.Reason,
            async (access, content, drafts, token) =>
            {
                var current = await drafts.Find(command.DraftId, token);
                RequireUnboundDraft(current, access.ActorId, expected);
                await HoldDraftSources(access, content, command.ContentRefs, token);
                var revision = await drafts.Save(command.DraftId, access.ActorId, command.OperationId, current, command.ContentRefs, token);
                return new(command.OperationId, command.DraftId, "DRAFT", revision.ToString(CultureInfo.InvariantCulture),
                    null, command.ContentRefs);
            }, ct);
    }

    public Task<FileDraftResultV1> BindDraftAsync(FileDraftBindV1 input, CancellationToken ct = default)
    {
        ValidateDraftCommand(input.OperationId, input.DraftId, input.ContentRefs, input.Reason);
        if (input.ContentRefs.Length == 0) throw Error("FILE_DRAFT_EMPTY", 400);
        ValidateBindingTarget(input.Target);
        var expected = Revision(input.ExpectedDraftSetVersion);
        var command = OperationsOwnerV1.Freeze(input);
        return WriteDraft(command.OperationId, command.DraftId, MainOwnerOperationJournalV1.Encode(command), command.Reason,
            async (access, content, drafts, token) =>
            {
                var current = await drafts.Find(command.DraftId, token);
                RequireUnboundDraft(current, access.ActorId, expected);
                var exact = await drafts.DraftRefs(command.DraftId, token);
                if (!exact.SequenceEqual(command.ContentRefs)) throw Error("FILE_DRAFT_SET_CHANGED", 412);

                var target = await Parent(access, command.Target, true, token);
                if (target.State != "DRAFT") throw Error("FILE_BINDING_TARGET_FROZEN", 409);
                await HoldDraftSources(access, content, exact, token);
                var revision = await drafts.Bind(command, current!, token);
                return new(command.OperationId, command.DraftId, "BOUND", revision.ToString(CultureInfo.InvariantCulture),
                    command.Target, exact);
            }, ct);
    }

    public Task<FileDraftResultV1> ReadDraftAsync(Guid id, CancellationToken ct = default)
    {
        if (id == Guid.Empty) throw Error("FILE_DRAFT_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", false, async (access, token) =>
        {
            access.RequireFileFields(false);
            var content = new FileContentStoreV1(db);
            await content.Fence(false, token);
            var draft = await new FileDraftStoreV1(db).Find(id, token);
            if (draft?.ActorId != access.ActorId) throw Error("FILE_DRAFT_FORBIDDEN", 403);
            var original = await new MainOwnerOperationJournalV1(db).Read(FileContentBytesV1.Owner, draft.OperationId, token)
                ?? throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
            return await DiscloseDraft(access, content, original, draft.OperationId, null, token);
        }, ct);
    }

    public Task<FileDraftResultV1> ReadDraftOperationAsync(Guid operation, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", false, async (access, token) =>
        {
            access.RequireFileFields(false);
            var content = new FileContentStoreV1(db);
            await content.Fence(false, token);
            var original = await new MainOwnerOperationJournalV1(db).Read(FileContentBytesV1.Owner, operation, token);
            if (original is null) return new FileDraftResultV1(operation, null, "UNKNOWN", null, null, []);
            return await DiscloseDraft(access, content, original, operation, null, token);
        }, ct);
    }

    public Task<FileContentRefV1[]> ListBindingsAsync(EngineeringRevisionRefV1 target, CancellationToken ct = default)
    {
        ValidateBindingTarget(target);
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "query", false, async (access, token) =>
        {
            access.RequireFileFields(false);
            var content = new FileContentStoreV1(db);
            await content.Fence(false, token);
            await Parent(access, target, false, token);
            var references = await new FileDraftStoreV1(db).BoundRefs(target, token);
            var visible = new List<FileContentRefV1>();
            foreach (var reference in references)
            {
                try { await ObserveDraftSource(access, content, reference, token); }
                catch (OrderPriceCorrectionException error) when (error is { Status: 403, Message: "OBJECT_FORBIDDEN" or "ACTION_FORBIDDEN" })
                { continue; }
                visible.Add(reference);
            }
            return visible.ToArray();
        }, ct);
    }

    private Task<FileDraftResultV1> WriteDraft(Guid operation, Guid draft, byte[] body, string reason,
        Func<OperationsAccessV1, FileContentStoreV1, FileDraftStoreV1, CancellationToken, Task<FileDraftResultV1>> apply,
        CancellationToken ct)
    {
        var content = new FileContentStoreV1(db);
        var journal = new MainOwnerOperationJournalV1(db);
        MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.FileContent, "edit", true, async (access, token) =>
        {
            if (original is not null) return await DiscloseDraft(access, content, original, operation, body, token);
            access.RequireFileFields(true);
            var result = await apply(access, content, new FileDraftStoreV1(db), token);
            await journal.Store(FileContentBytesV1.Owner, DraftContract, operation, access.ActorId, body, result, [draft],
                new { action = result.State, reason, actor = access.ActorId, result }, token);
            return result;
        }, ct, actionAtFence: async token =>
        {
            // 在文件写锁内取原键，等待前一个提交后重新读取。
            await content.Fence(true, token);
            original = await journal.Read(FileContentBytesV1.Owner, operation, token);
            return original is null ? "edit" : "query";
        });
    }

    private async Task<FileDraftResultV1> DiscloseDraft(OperationsAccessV1 access, FileContentStoreV1 content,
        MainOwnerOperationJournalV1.Original original, Guid operation, byte[]? body, CancellationToken ct)
    {
        access.RequireFileFields(false);
        if (original.Actor != access.ActorId || original.Contract != DraftContract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (body is not null && !MainOwnerOperationJournalV1.Matches(original, body)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<FileDraftResultV1>(original.Result);
        if (result?.DraftId is not { } draft || result.OperationId != operation || !original.Objects.SequenceEqual(new[] { draft }))
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        if (result.Target is { } target) await Parent(access, target, false, ct);
        await ObserveDraftSources(access, content, result.ContentRefs, ct);
        return result;
    }

    private async Task ObserveDraftSources(OperationsAccessV1 access, FileContentStoreV1 content,
        FileContentRefV1[] references, CancellationToken ct)
    {
        foreach (var reference in references)
            await ObserveDraftSource(access, content, reference, ct);
    }

    private async Task ObserveDraftSource(OperationsAccessV1 access, FileContentStoreV1 content, FileContentRefV1 reference, CancellationToken ct)
    {
        var saved = await ReadVisibleContent(access, content, reference.Id, ct);
        if (saved.ContentRef != reference) throw Error("FILE_REFERENCE_CHANGED", 412);
    }

    private async Task HoldDraftSources(OperationsAccessV1 access, FileContentStoreV1 content,
        FileContentRefV1[] references, CancellationToken ct)
    {
        if (references.Length == 0) return;
        var source = await access.ActionAsync(OperationsOwnerV1.EngineeringRevision, "edit", ct);
        foreach (var reference in references)
        {
            var saved = await ReadVisibleContent(access, content, reference.Id, ct);
            if (saved.ContentRef != reference) throw Error("FILE_REFERENCE_CHANGED", 412);
            if (saved.Availability != "AVAILABLE") throw Error("FILE_CONTENT_NOT_AVAILABLE", 409);
            await source.RequireEngineeringRevision(saved.Parent.Id, ct);
            source.AtCommit(token => source.RequireEngineeringRevision(saved.Parent.Id, token));
            var row = await content.Row(reference.Id, ct);
            await VerifyStored(row.StorePath, row.ByteLength, row.Digest, ct);
        }
    }

    private static void RequireUnboundDraft(FileDraftStoreV1.Draft? draft, Guid actor, long? expected)
    {
        if (draft is not null && draft.ActorId != actor) throw Error("FILE_DRAFT_FORBIDDEN", 403);
        if (draft?.Bound == true) throw Error("FILE_DRAFT_ALREADY_BOUND", 409);
        if (draft?.Revision != expected) throw Error("FILE_DRAFT_SET_CHANGED", 412);
    }

    private static void ValidateDraftCommand(Guid operation, Guid draft, FileContentRefV1[]? references, string reason)
    {
        if (operation == Guid.Empty || draft == Guid.Empty || references is null) throw Error("FILE_DRAFT_COMMAND_INVALID", 400);
        EngineeringRoutingValuesV1.Text(reason);
        var ids = new HashSet<Guid>();
        foreach (var reference in references)
        {
            if (!ids.Add(ValidateContentRef(reference).Id))
                throw Error("FILE_REFERENCE_INVALID", 400);
        }
    }

    private static FileContentRefV1 ValidateContentRef(FileContentRefV1? reference)
    {
        if (reference is null || reference.Owner != FileContentBytesV1.Owner || reference.Id == Guid.Empty
            || reference.Version != "1" || reference.HashAlgorithm != "SHA256" || !ValidDigest(reference.Digest)
            || reference.ByteLength is < 1 or > 20971520 || string.IsNullOrWhiteSpace(reference.MediaType))
            throw Error("FILE_REFERENCE_INVALID", 400);
        return reference;
    }

    private static void ValidateBindingTarget(EngineeringRevisionRefV1? target)
    {
        if (target is null || target.Owner != EngineeringRevisionValuesV1.RevisionOwner || target.Id == Guid.Empty
            || !ValidDigest(target.Digest)) throw Error("FILE_BINDING_TARGET_INVALID", 400);
        Revision(target.Version);
    }

    private static bool ValidDigest(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
