using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Wf;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Core.Services.Wf;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FlowDraftCreateV1(Guid OperationId, Guid SourceVersionId, string ExpectedHeadVersion);
public sealed record FlowDraftSourceV1(Guid VersionId, int Version, string Name, string SchemaDigest);
public sealed record FlowDraftContextV1(Guid DefinitionId, string HeadVersion, Guid? DraftVersionId, FlowDraftSourceV1[] Sources);
public sealed record FlowDraftCreatedV1(string Owner, Guid OperationId, Guid SourceVersionId, string SourceDigest, DefinitionDraftDto Draft);
public sealed record FlowDraftOperationV1(Guid OperationId, string State, FlowDraftCreatedV1? Result);

/// <summary>
/// Explicit next-draft action for the existing flow designer. Uses Main's current native IAM/transaction
/// gate and original journal; this is not WFC-1 or the unregistered OA-APP WF_AUTH protocol.
/// </summary>
public sealed class FlowDraftOwnerV1(CP6Context db, IHttpContextAccessor http)
{
    private const string Owner = "Main.Workflow.Draft.v1", Contract = "Main.Workflow.Draft.create.v1";
    private static OrderPriceCorrectionException Error(string code, int status = 409) => new(code, status);
    private static string Digest(string text) => MainOwnerOperationJournalV1.Digest(System.Text.Encoding.UTF8.GetBytes(text));
    private async Task<(Wf_FlowDef Head, Wf_FlowDefVersion[] Versions, FlowDraftContextV1 Context)> Snapshot(string key, CancellationToken ct, Guid? exclude = null)
    {
        var head = await db.Wf_FlowDefs.AsNoTracking().SingleOrDefaultAsync(x => x.FlowKey == key, ct) ?? throw Error("DEFINITION_NOT_FOUND", 404);
        var versions = await db.Wf_FlowDefVersions.AsNoTracking().Where(x => x.FlowDefId == head.Id && (exclude == null || x.Id != exclude)).OrderBy(x => x.Version).ToArrayAsync(ct);
        var token = MainOwnerOperationJournalV1.Digest(MainOwnerOperationJournalV1.Encode(new
        {
            db.CurrentTenantId, head.Id, head.FlowKey, head.FlowName, head.SchemaJson, head.FormKey, head.FunctionId,
            head.FlowCode, head.Enable, head.Version, head.ModifyDate,
            Versions = versions.Select(x => new { x.Id, x.Version, x.Status, x.FlowNameSnapshot, x.SchemaJson, x.RowVersion, x.PublishedAtUtc, x.PublishedBy })
        }));
        return (head, versions, new(head.Id, token,
            versions.SingleOrDefault(x => x.Status == WfDefinitionVersionStatus.Draft)?.Id,
            versions.Where(x => x.Status == WfDefinitionVersionStatus.Published).OrderByDescending(x => x.Version)
                .Select(x => new FlowDraftSourceV1(x.Id, x.Version, x.FlowNameSnapshot, Digest(x.SchemaJson))).ToArray()));
    }
    public async Task<FlowDraftContextV1> ContextAsync(string key, CancellationToken ct = default)
    {
        await Actor(false, ct);
        return (await Snapshot(key, ct)).Context;
    }

    private async Task<Guid> Actor(bool edit, CancellationToken ct)
    {
        try
        {
        var id = CurrentPermissionContext.RequireCurrentUser(db, http);
        var current = await PostgreSqlPermissionReader.ReadAsync(db, id, ct);
        if (current.UserId != id || string.IsNullOrWhiteSpace(current.UserName)
            || !await db.Sys_Users.AnyAsync(x => x.Id == id && x.Enable && (x.LockedUntil == null || x.LockedUntil <= DateTime.Now), ct)
            || !await db.Sys_Tenants.AnyAsync(x => x.Id == db.CurrentTenantId && x.Enable && (x.ExpireDate == null || x.ExpireDate > DateTime.Now), ct))
            throw Error("REAUTH_REQUIRED", 401);
        if (edit && (!current.ActionKeys.Contains("oa-designer:edit") || !await
            (from a in db.Sys_MenuActions join m in db.Sys_Menus on a.MenuId equals m.MenuId
             where m.Enable && m.MenuKey == "oa-designer" && a.ActionCode == "edit" select a).AnyAsync(ct)))
            throw Error("ACTION_FORBIDDEN", 403);
        return id;
        }
        catch (CurrentPermissionUnavailableException error) { throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error); }
    }
    private async Task Fence(CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '10s'", ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var helper = db.GetService<ISqlGenerationHelper>();
        string Table<T>() { var e = db.Model.FindEntityType(typeof(T))!; return helper.DelimitIdentifier(e.GetTableName()!, e.GetSchema() ?? "public"); }
        // Includes existing raw SQL/EF publishers and editors, including absent-draft races.
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE " + Table<Wf_FlowDef>() + "," + Table<Wf_FlowDefVersion>() + " IN SHARE ROW EXCLUSIVE MODE", ct);
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
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, http.HttpContext?.RequestAborted ?? CancellationToken.None); ct = linked.Token;
        if (request == null || request.OperationId == Guid.Empty || request.SourceVersionId == Guid.Empty || request.ExpectedHeadVersion?.Length != 64)
            throw Error("DRAFT_INPUT_INVALID", 400);
        Entry(); var input = MainOwnerOperationJournalV1.Encode(new { Key = key, Request = request });
        await using var tx = await db.Database.BeginTransactionAsync(ct); var committing = false;
        try
        {
            await Fence(ct); var actor = await Actor(false, ct); var journal = new MainOwnerOperationJournalV1(db);
            var previous = await journal.Read(Owner, request.OperationId, ct);
            if (previous != null)
            {
                var saved = Original(previous, actor, request.OperationId, input);
                if (!await db.Wf_FlowDefs.AnyAsync(x => x.Id == saved.Draft.DefinitionId && x.FlowKey == key, ct)) throw Error("DEFINITION_NOT_FOUND", 404);
                return saved;
            }
            await Actor(true, ct);
            var snapshot = await Snapshot(key, ct);
            if (snapshot.Context.HeadVersion != request.ExpectedHeadVersion) throw Error("DEFINITION_CHANGED", 412);
            if (snapshot.Context.DraftVersionId != null) throw Error("DRAFT_EXISTS");
            var source = snapshot.Versions.SingleOrDefault(x => x.Id == request.SourceVersionId && x.Status == WfDefinitionVersionStatus.Published)
                ?? throw Error("PUBLISHED_SOURCE_REQUIRED", 422);
            var draft = new Wf_FlowDefVersion
            {
                Id = Guid.NewGuid(), FlowDefId = snapshot.Head.Id, Version = checked(snapshot.Versions.Max(x => x.Version) + 1),
                Status = WfDefinitionVersionStatus.Draft, FlowNameSnapshot = source.FlowNameSnapshot,
                SchemaJson = source.SchemaJson, Creator = actor.ToString()
            };
            db.Wf_FlowDefVersions.Add(draft); await db.SaveChangesAsync(ct);
            var result = new FlowDraftCreatedV1(Owner, request.OperationId, source.Id, Digest(source.SchemaJson),
                new(snapshot.Head.Id, draft.Id, draft.Version, draft.FlowNameSnapshot, draft.SchemaJson, draft.RowVersion, draft.Status));
            await journal.Store(Owner, Contract, request.OperationId, actor, input, result, [snapshot.Head.Id],
                new { Kind = "CREATE_DRAFT", SourceVersionId = source.Id, SourceDigest = result.SourceDigest, request.ExpectedHeadVersion }, ct);
            if ((await Snapshot(key, ct, draft.Id)).Context.HeadVersion != request.ExpectedHeadVersion)
                throw Error("DEFINITION_CHANGED", 412);
            var persisted = await db.Wf_FlowDefVersions.AsNoTracking().SingleAsync(x => x.Id == draft.Id, ct);
            if (persisted.FlowDefId != draft.FlowDefId || persisted.Version != draft.Version || persisted.Status != WfDefinitionVersionStatus.Draft
                || persisted.SchemaJson != source.SchemaJson || persisted.FlowNameSnapshot != source.FlowNameSnapshot
                || !persisted.RowVersion.AsSpan().SequenceEqual(draft.RowVersion)) throw Error("DEFINITION_CHANGED", 412);
            if (await Actor(true, ct) != actor) throw Error("REAUTH_REQUIRED", 401);
            ct.ThrowIfCancellationRequested(); committing = true; await tx.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            try { await tx.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new OrderPriceCorrectionException("DRAFT_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            if (committing) throw new OrderPriceCorrectionException("DRAFT_OUTCOME_UNKNOWN", 503, error);
            if (error is Npgsql.NpgsqlException or CurrentPermissionUnavailableException) throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503, error);
            throw;
        }
        finally { foreach (var entry in db.ChangeTracker.Entries<Wf_FlowDefVersion>().ToArray()) entry.State = EntityState.Detached; }
    }
    public async Task<FlowDraftOperationV1> OperationAsync(string key, Guid operation, CancellationToken ct = default)
    {
        Entry(); await using var tx = await db.Database.BeginTransactionAsync(ct);
        var actor = await Actor(false, ct); var original = await new MainOwnerOperationJournalV1(db).Read(Owner, operation, ct);
        if (original == null) return new(operation, "NOT_OBSERVED", null);
        var result = Original(original, actor, operation, null);
        if (!await db.Wf_FlowDefs.AnyAsync(x => x.Id == result.Draft.DefinitionId && x.FlowKey == key, ct)) throw Error("DEFINITION_NOT_FOUND", 404);
        return new(operation, "SUCCEEDED", result);
    }
}
