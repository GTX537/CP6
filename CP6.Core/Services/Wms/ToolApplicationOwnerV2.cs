using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Oa;
using CP6.Core.Services.Space;
using CP6.Core.Services.Sys;
using static CP6.Core.Services.Wms.ToolInstanceStoreV1;
using static CP6.Core.Services.Wms.ToolApplicationReceiptStoreV2;

namespace CP6.Core.Services.Wms;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ToolApplicationCommandV2(Guid OperationId, Guid ReceiptStreamId,
    [property: JsonRequired] Guid? ExpectedReceiptId, string Reason);
public sealed record ToolApplicationContextV2(Guid TenantId, Guid ToolId, string Etag, string State, Guid? ReceiptStreamId,
    Guid? DecisionId, string? Decision, DateTimeOffset? DecidedAt, ApprovalApplicationReceiptV2? Receipt,
    ToolDispositionObligationsV1 Obligations, bool IsDiscarded, bool CanFinalize = false);
public sealed record ToolApplicationResultV2(Guid OperationId, Guid ObjectId, ApprovalApplicationReceiptV2 Receipt);
public sealed record ToolApplicationOperationV2(Guid OperationId, string State, ToolApplicationResultV2? Result = null);

public sealed class ToolApplicationOwnerV2(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 scope)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static async Task<T> Guard<T>(Func<Task<T>> action)
    { try { return await action(); } catch (ScopeAuthorizationExceptionV1 e) { throw Error(e.Message, e.Status); } }
    private sealed record Context(OperationsAccessV1 Access, ToolDispositionContextV1 Disposition, Source? Source,
        ApprovalApplicationReceiptV2? Receipt, ToolApplicationReceiptStoreV2 Store, ScopeAuthorizationLeaseV1 Lease)
    {
        internal string Etag => "\"tap2-" + ApprovalCanonicalV2.Hash(ApprovalCanonicalV2.Bytes(new { Disposition.Etag,
            Source?.OriginalHash, Source?.Stream, Receipt?.ReceiptId })) + "\"";
    }
    private async Task<Context> Prepare(OperationsAccessV1 a, Guid tool, bool write, CancellationToken ct)
    {
        await scope.Root(ct);
        var lease = await scope.AcquireAsync(ct); a.AtCommit(t => scope.ValidateAsync(lease, t));
        var disposition = await new ToolDispositionOwnerV1(db, owner).ReadOwnedAsync(a, tool, write, ct);
        a.RequireToolFieldRead("Remarks");
        var store = new ToolApplicationReceiptStoreV2(db, scope); await store.Ready(ct);
        var source = await store.ForTool(tool, ct); var receipt = source == null ? null : await store.Latest(source.Stream, ct);
        return new(a, disposition, source, receipt, store, lease);
    }
    public Task<ToolApplicationContextV2> ContextAsync(Guid tool, CancellationToken ct = default)
        => Guard(() => Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", false, async (a, t) => {
            var c = await Prepare(a, tool, false, t); var d = c.Source?.Decision;
            return new ToolApplicationContextV2(db.CurrentTenantId, tool, c.Etag, c.Source == null ? "NOT_RECEIVED" : "RECEIVED",
                c.Source?.Stream, d?.DecisionId, d?.Decision, d?.DecidedAt, c.Receipt, c.Disposition.Obligations, c.Disposition.IsDiscarded);
        }, ct));
    private async Task<(string State, string Code)> Qualify(Context c, CancellationToken ct)
    {
        var s = c.Source!; var d = s.Decision; var request = c.Disposition.Request;
        if (request == null || request.Reference.Id.ToString() != d.Subject.SubjectId || request.Reference.Version != d.Subject.BusinessRevision
            || request.Reference.Digest != d.Subject.ContentDigest.Value || c.Disposition.IsDiscarded) return ("STALE", "SUBJECT_NO_LONGER_CURRENT");
        if (d.Decision != "APPROVED") return ("BLOCKED", d.Decision == "REJECTED" ? "APPROVAL_REJECTED" : "APPROVAL_WITHDRAWN");
        ToolDispositionPolicyCommandV1 policy;
        try { policy = await new ToolDispositionApprovalOwnerV1(db, owner, scope).ApplicationPolicy(c.Access, s.Tool, s.Submission, ct); }
        catch (OrderPriceCorrectionException e) when (e.Status is 412 or 422) { return ("BLOCKED", e.Message); }
        if (policy.SeparateRequesterAndHandler && s.Submission.SeparationSubjectIds.Contains(c.Access.ActorId)) return ("BLOCKED", "APPLICATION_SOD_REQUIRED");
        if (c.Disposition.Obligations.UnknownOwners.Any(x => x != "OA_APPROVAL_OWNER")) return ("BLOCKED", "OBLIGATION_SOURCES_UNPROVEN");
        if (c.Disposition.Obligations.Local.Length != 0) return ("BLOCKED", "OBLIGATIONS_OPEN");
        // No trusted closure participant is registered in the current native Owner.
        return ("BLOCKED", "APPLICATION_EFFECT_UNPROVEN");
    }
    public Task<ToolApplicationResultV2> ApplyAsync(Guid tool, string? etag, ToolApplicationCommandV2 input, CancellationToken ct = default)
    {
        if (input.OperationId == Guid.Empty || input.ReceiptStreamId == Guid.Empty || string.IsNullOrWhiteSpace(input.Reason)
            || input.Reason.EnumerateRunes().Count() > 500) throw Error("APPLICATION_INPUT_INVALID", 400);
        try { new UTF8Encoding(false, true).GetByteCount(input.Reason); } catch (EncoderFallbackException) { throw Error("APPLICATION_INPUT_INVALID", 400); }
        var command = OperationsOwnerV1.Freeze(input);
        byte[] bytes;
        try { bytes = MainOwnerOperationJournalV1.Encode(new { contract = Contract, environment = scope.EnvironmentId, tool, etag, command }); }
        catch (ScopeAuthorizationExceptionV1 e) { throw Error(e.Message, e.Status); }
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Guard(() => Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "dispose", true, async (a, t) => {
            var c = await Prepare(a, tool, original == null, t);
            if (original != null) return await Disclose(c, command.OperationId, original, bytes, t);
            if (string.IsNullOrEmpty(etag)) throw Error("IF_MATCH_REQUIRED", 428);
            if (c.Etag != etag || c.Receipt?.ReceiptId != command.ExpectedReceiptId) throw Error("APPLICATION_CONTEXT_CHANGED", 412);
            var s = c.Source ?? throw Error("APPROVAL_FACT_NOT_RECEIVED", 409);
            if (s.Stream != command.ReceiptStreamId) throw Error("APPLICATION_SOURCE_MISMATCH", 409);
            await a.RequireToolInstance(db, tool, t);
            ApprovalApplicationReceiptV2 receipt; (string State, string Code)? evaluated = null;
            if (c.Receipt is { Application: not "BLOCKED" }) receipt = c.Receipt;
            else
            {
                var outcome = await Qualify(c, t); evaluated = outcome;
                var witnesses = a.Authorities.DistinctBy(x => (x.Resource, x.Action)).Select(x => new {
                    x.Resource, x.Action, x.Source.SourcePermissionVersion, x.OrganizationVersion }).ToArray();
                receipt = await c.Store.Append(s, a.ActorId, command.OperationId, c.Receipt, outcome.State, outcome.Code,
                    c.Disposition.Request?.Reference.Version ?? s.Decision.Subject.BusinessRevision,
                    new { profile = "Main.Wms.ToolApplication/2", command, a.ActorId, authorities = witnesses,
                        sourceHash = s.OriginalHash, s.EvidenceMode, disposition = c.Disposition, outcome.State, outcome.Code }, t);
            }
            var result = new ToolApplicationResultV2(command.OperationId, tool, receipt);
            await journal.Store(Journal, Contract, command.OperationId, a.ActorId, bytes, result, [tool],
                new { command, receipt.ReceiptId, receipt.PayloadDigest, sourceHash = s.OriginalHash }, t);
            var q = await a.SourceAsync(OperationsOwnerV1.ToolInstance, t);
            await new ToolInstanceStoreV1(db).HoldExactAsync(q, c.Disposition.Native, t);
            a.AtCommit(async token => {
                await c.Store.Ready(token);
                if (evaluated is {} initial && await Qualify(c, token) != initial) throw Error("APPLICATION_CONTEXT_CHANGED", 412);
                if ((await c.Store.ForTool(tool, token))?.OriginalHash != s.OriginalHash
                    || (await c.Store.Latest(s.Stream, token))?.ReceiptId != receipt.ReceiptId
                    || (await new ToolDispositionOwnerV1(db, owner).ReadOwnedAsync(a, tool, true, token)).Etag != c.Disposition.Etag)
                    throw Error("APPLICATION_CONTEXT_CHANGED", 412);
                await a.RequireToolInstance(db, tool, token);
                await scope.ValidateAsync(c.Lease, token);
            });
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Journal, command.OperationId, t); return original == null ? "dispose" : "query"; }));
    }
    private async Task<ToolApplicationResultV2> Disclose(Context c, Guid operation, MainOwnerOperationJournalV1.Original original, byte[]? bytes, CancellationToken ct)
    {
        if (original.Actor != c.Access.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (bytes != null && !MainOwnerOperationJournalV1.Matches(original, bytes)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<ToolApplicationResultV2>(original.Result);
        var persisted = result == null ? null : await c.Store.Exact(result.Receipt.ReceiptId, ct);
        if (original.State != "COMMITTED" || result == null || result.OperationId != operation || result.ObjectId != c.Disposition.ToolId
            || original.Objects.Length != 1 || original.Objects[0] != result.ObjectId || c.Source?.Stream != result.Receipt.ReceiptStreamId
            || persisted == null || !ApprovalCanonicalV2.Bytes(result.Receipt).AsSpan().SequenceEqual(ApprovalCanonicalV2.Bytes(persisted)))
            throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
        return result;
    }
    public Task<ToolApplicationOperationV2> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Guard(() => Authority.ExecuteAsync(OperationsOwnerV1.ToolInstance, "query", close, async (a, t) => {
            var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Journal, operation, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                if (original.State == "CLOSED") return new ToolApplicationOperationV2(operation, "CLOSED");
                if (original.Objects.Length != 1) throw Error("ORIGINAL_OPERATION_UNAVAILABLE", 503);
                var c = await Prepare(a, original.Objects[0], false, t);
                return new(operation, "COMMITTED", await Disclose(c, operation, original, null, t));
            }
            if (!close) return new(operation, "UNKNOWN"); var result = new ToolApplicationOperationV2(operation, "CLOSED");
            await journal.Store(Journal, Contract, operation, a.ActorId, null, result, [], new { action = "close-tool-application" }, t); return result;
        }, ct));
    }
}
