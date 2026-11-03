using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FlowDraftCreateV1(Guid OperationId, Guid SourceVersionId, string ExpectedHeadVersion);
public sealed record FlowDraftSourceV1(Guid VersionId, int Version, string Name, string SchemaDigest);
public sealed record FlowDraftContextV1(Guid DefinitionId, string HeadVersion, Guid? DraftVersionId, FlowDraftSourceV1[] Sources);
public sealed record FlowDraftCreatedV1(string Owner, Guid OperationId, Guid SourceVersionId, string SourceDigest, DefinitionDraftDto Draft);
public sealed record FlowDraftOperationV1(Guid OperationId, string State, FlowDraftCreatedV1? Result);

/// <summary>
/// Retired v1 writer: preserves exact committed-result reads under current IAM and field permissions.
/// New next-draft commands belong to FlowAuthoringOwnerV2; this reader never creates a draft.
/// </summary>
public sealed class FlowDraftOwnerV1(CP6Context db, FlowAuthoringOwnerV2 authoring)
{
    private const string Owner = "Main.Workflow.Draft.v1", Contract = "Main.Workflow.Draft.create.v1";
    private static OrderPriceCorrectionException Error(string code, int status = 409) => new(code, status);
    public Task<FlowDraftContextV1> ContextAsync(string key, CancellationToken ct = default)
        => Task.FromException<FlowDraftContextV1>(Error(LegacyDefinitionWrites.Code, 410));

    private async Task<UserPermissionContext> Actor(CancellationToken ct)
    {
        try { return await authoring.LegacyActorAsync(ct); }
        catch (ScopeAuthorizationExceptionV1 e) { throw new OrderPriceCorrectionException(e.Message, e.Status, e); }
        catch (CurrentPermissionUnavailableException error) { throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }
    private void Entry()
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null
            || db.ChangeTracker.HasChanges() || db.ChangeTracker.Entries<Wf_FlowDef>().Any() || db.ChangeTracker.Entries<Wf_FlowDefVersion>().Any())
            throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    }
    private static FlowDraftCreatedV1 Original(MainOwnerOperationJournalV1.Original row, Guid actor, Guid operation, byte[]? input)
    {
        if (row.Actor != actor || row.Contract != Contract) throw Error("OBJECT_FORBIDDEN", 403);
        if (input != null && !MainOwnerOperationJournalV1.Matches(row, input)) throw Error("COMMAND_BODY_CONFLICT");
        var result = JsonSerializer.Deserialize<FlowDraftCreatedV1>(row.Result) ?? throw Error("ORIGINAL_RESULT_INVALID", 503);
        if (result.Owner != Owner || result.OperationId != operation) throw Error("ORIGINAL_RESULT_INVALID", 503);
        return result;
    }
    public async Task<FlowDraftCreatedV1> CreateAsync(string key, FlowDraftCreateV1 request, CancellationToken ct = default)
    {
        if (request.OperationId == Guid.Empty || request.SourceVersionId == Guid.Empty || request.ExpectedHeadVersion?.Length != 64)
            throw Error("DRAFT_INPUT_INVALID", 400);
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var actor = await Actor(ct);
        var previous = await new MainOwnerOperationJournalV1(db).Read(Owner, request.OperationId, ct);
        if (previous == null) throw Error(LegacyDefinitionWrites.Code, 410);
        // A retired writer still resolves the exact already-committed intent, without creating or replaying a write.
        var saved = Original(previous, actor.UserId, request.OperationId, MainOwnerOperationJournalV1.Encode(new { Key = key, Request = request }));
        if (!await db.Wf_FlowDefs.AnyAsync(x => x.Id == saved.Draft.DefinitionId && x.FlowKey == key, ct)) throw Error("DEFINITION_NOT_FOUND", 404);
        await Disclosure(actor, ct); return saved;
    }
    public async Task<FlowDraftOperationV1> OperationAsync(string key, Guid operation, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var actor = await Actor(ct); var original = await new MainOwnerOperationJournalV1(db).Read(Owner, operation, ct);
        if (original == null) return new(operation, "NOT_OBSERVED", null);
        var result = Original(original, actor.UserId, operation, null);
        if (!await db.Wf_FlowDefs.AnyAsync(x => x.Id == result.Draft.DefinitionId && x.FlowKey == key, ct)) throw Error("DEFINITION_NOT_FOUND", 404);
        await Disclosure(actor, ct); return new(operation, "SUCCEEDED", result);
    }
    private async Task Disclosure(UserPermissionContext actor, CancellationToken ct)
    {
        try { await authoring.EnsureLegacyOriginalReadAsync(actor, ct); }
        catch (ScopeAuthorizationExceptionV1 e) { throw new OrderPriceCorrectionException(e.Message, e.Status, e); }
    }

}
