using System.Globalization;
using System.Text.Json;
using CP6.Core.Services.Integration;
using Microsoft.EntityFrameworkCore;
using static CP6.Core.Services.Wms.Transfer.TransferEnvironmentStoreV1;
using Store = CP6.Core.Services.Wms.Transfer.TransferRequestStoreV1;

namespace CP6.Core.Services.Wms.Transfer;

public sealed partial class TransferRequestOwnerV1
{
    public Task<TransferRequestResultV1> CreateAsync(TransferRequestCreateV1 input, CancellationToken ct = default)
    {
        Command(input.OperationId, input.Reason); var c = OperationsOwnerV1.Freeze(input); c = c with { Lines = Inputs(c.Lines) };
        if (string.IsNullOrWhiteSpace(c.DraftKey) || c.DraftKey.Length > 200) throw Error("TRANSFER_DRAFT_KEY_INVALID", 400);
        return Execute(c.OperationId, "add", new { Action = "CREATE", Command = c }, async (a, registration, t) =>
        {
            if (await Store.Heads(db).AnyAsync(x => x.CreatorId == a.ActorId && x.DraftKey == c.DraftKey, t)) throw Error("TRANSFER_DRAFT_KEY_EXISTS", 409);
            var id = Guid.NewGuid(); var resolved = await Resolve(a, registration, c.Lines, id, "1", [], t);
            var budget = await Budget(registration.Original.Deployment, resolved.Select(x => x.BudgetKey), t);
            var lines = await Bind(budget, id, "1", resolved, t);
            var view = Seal(new(id, c.DraftKey, a.ActorId, a.ActorId, c.DepartmentId, "1", "1", "", "DRAFT", lines, "", null, null,
                registration.Original.Deployment, []), true);
            return (view, true);
        }, ct);
    }
    public Task<TransferRequestResultV1> EditAsync(Guid id, TransferRequestEditV1 input, CancellationToken ct = default)
    {
        Command(input.OperationId, input.Reason); var c = OperationsOwnerV1.Freeze(input); c = c with { Lines = Inputs(c.Lines) };
        return Execute(c.OperationId, "edit", new { Action = "EDIT", Id = id, Command = c }, async (a, registration, t) =>
        {
            var v = await new Store(db).Read(id, t); await Visible(a, v, t); Expected(v, c.ExpectedRevision, c.Etag);
            if (v.State != "DRAFT") throw Error("TRANSFER_DRAFT_REQUIRED", 409);
            if (Store.Bytes(v.Lines.Select(x => x.Input).ToArray()).AsSpan().SequenceEqual(Store.Bytes(c.Lines))) return (v, false);
            var version = Next(v.Version); var resolved = await Resolve(a, registration, c.Lines, id, version, v.Lines, t);
            var budget = await Budget(v.Deployment, v.Lines.Select(x => x.BudgetKey).Concat(resolved.Select(x => x.BudgetKey)), t);
            await Release(budget, v, t); var lines = await Bind(budget, id, version, resolved, t);
            return (Seal(v with { AuthorId = a.ActorId, Version = version, Revision = Next(v.Revision), Lines = lines }, true), true);
        }, ct);
    }
    public Task<TransferRequestResultV1> ActionAsync(Guid id, string action, TransferRequestActionV1 input, CancellationToken ct = default)
    {
        Command(input.OperationId, input.Reason); var c = OperationsOwnerV1.Freeze(input);
        var permission = action switch { "SUBMIT" => "submit", "APPROVE" or "REJECT" => "approve", "WITHDRAW" => "withdraw", _ => throw Error("TRANSFER_ACTION_INVALID", 400) };
        return Execute(c.OperationId, permission, new { Action = action, Id = id, Command = c }, async (a, registration, t) =>
        {
            var v = await new Store(db).Read(id, t); await Visible(a, v, t); Expected(v, c.ExpectedRevision, c.Etag);
            if (v.ContentDigest != c.ContentDigest) throw Error("TRANSFER_CONTENT_CHANGED", 412);
            if (action == "WITHDRAW")
            {
                if (v.State is not ("DRAFT" or "SUBMITTED")) throw Error("TRANSFER_WITHDRAWAL_FORBIDDEN", 409);
                var budget = await Budget(v.Deployment, v.Lines.Select(x => x.BudgetKey), t); await Release(budget, v, t);
                return (Seal(v with { State = "WITHDRAWN", Revision = Next(v.Revision) }, false), true);
            }
            if (action == "SUBMIT" ? v.State != "DRAFT" : v.State != "SUBMITTED") throw Error("TRANSFER_STATE_CONFLICT", 409);
            if (action is "APPROVE" or "REJECT" && (a.ActorId == v.AuthorId || a.ActorId == v.CreatorId || a.ActorId == v.SubmitterId))
                throw Error("TRANSFER_INDEPENDENT_APPROVER_REQUIRED", 403);
            // Current source facts can change after a draft was saved; re-resolve at the decision boundary.
            var current = await Resolve(a, registration, v.Lines.Select(x => x.Input).ToArray(), id, v.Version, v.Lines, t);
            for (var i = 0; i < current.Length; i++)
                if (current[i].Product != v.Lines[i].Product || current[i].BaseQuantity != v.Lines[i].BaseQuantity
                    || current[i].SourceSiteRef != v.Lines[i].SourceSiteRef || current[i].DestinationSiteRef != v.Lines[i].DestinationSiteRef)
                    throw Error("TRANSFER_SOURCE_CHANGED", 412);
            var active = await Budget(v.Deployment, v.Lines.Select(x => x.BudgetKey), t);
            foreach (var l in v.Lines)
                if (await active.ReadState(l.BudgetKey, l.BindingId, t) != "ACTIVE") throw Error("TRANSFER_SOURCE_BINDING_CLOSED", 409);
            if (action == "SUBMIT") return (Seal(v with { State = "SUBMITTED", SubmitterId = a.ActorId, Revision = Next(v.Revision) }, false), true);
            var decision = new TransferRequestApprovalV1(Guid.NewGuid(), a.ActorId, v.Version, v.ContentDigest, DateTimeOffset.UtcNow, action == "APPROVE" ? "APPROVED" : "REJECTED");
            if (action == "REJECT") await Release(active, v, t);
            return (Seal(v with { State = decision.Decision, Approval = decision, Revision = Next(v.Revision) }, false), true);
        }, ct);
    }
    private static TransferRequestViewV1 Seal(TransferRequestViewV1 v, bool content)
    {
        var digest = content ? Store.Digest(new { schema = "MAIN_TRANSFER_REQUEST_1", v.Id, v.Version, mode = "DIRECT_MOVE",
            lines = v.Lines.Select(x => new { x.Id, x.Input, x.Product, x.SourceSiteRef, x.DestinationSiteRef, x.BaseQuantity, x.DemandRef, x.BindingId }) }) : v.ContentDigest;
        return v with { ContentDigest = digest, Etag = "\"" + Store.Digest(new { v.Id, v.Revision, digest, v.State }) + "\"", AllowedActions = [] };
    }
    private Task<TransferDemandBudgetV1> Budget(TransferDeploymentV1 d, IEnumerable<string> keys, CancellationToken ct)
        => TransferDemandBudgetV1.Open(db, new(d.EnvironmentId, d.Mode, d.SandboxScopeId), keys, ct);
    private static async Task<TransferRequestLineV1[]> Bind(TransferDemandBudgetV1 budget, Guid request, string version,
        ResolvedLine[] lines, CancellationToken ct)
    {
        var result = new List<TransferRequestLineV1>();
        foreach (var l in lines)
        {
            var p = l.Product; var demand = new TransferDemandBudgetV1.LocalDemand(Guid.NewGuid(), l.BudgetKey, "1", request, l.Id,
                new(p.ProductRef.Owner, p.ProductRef.Id, p.ProductRef.Version, p.ProductRef.Digest), new(p.BaseUomRef.Id, p.BaseUomRef.Version),
                l.SourceSiteRef.Id, l.DestinationSiteRef.Id, new(p.OriginPurposeRef.Id, p.OriginPurposeRef.Version),
                [new(l.Input.DestinationWarehouseId, l.Input.DestinationLocationId)], l.BaseQuantity, [new("0.00000000", l.BaseQuantity)], "OPEN",
                new("WMS_TRANSFER", request, version, Store.Digest(new { request, version, l.Id, l.Input, l.BaseQuantity })));
            var saved = await budget.RegisterLocal(demand, "0", ct); var binding = Guid.NewGuid();
            var original = await budget.Bind(l.BudgetKey, new(binding, request, l.Id, saved.Ref, l.BaseQuantity,
                [new("whole", new("0.00000000", l.BaseQuantity), new("0.00000000", l.BaseQuantity))]), ct);
            result.Add(new(l.Id, l.Input, l.Product, l.SourceSiteRef, l.DestinationSiteRef, l.BaseQuantity, l.BudgetKey,
                new(saved.Ref.Owner, saved.Ref.Id, saved.Ref.Version, saved.Ref.Digest), saved.Body, binding, original));
        }
        return result.ToArray();
    }
    private static async Task Release(TransferDemandBudgetV1 budget, TransferRequestViewV1 v, CancellationToken ct)
    {
        foreach (var l in v.Lines)
        {
            await budget.Release(l.BudgetKey, l.BindingId, v.Id, l.Id, ct);
            var demand = JsonSerializer.Deserialize<TransferDemandBudgetV1.LocalDemand>(l.DemandOriginalJson, Store.Json)!;
            await budget.RegisterLocal(demand with { Id = l.DemandRef.Id, Version = Next(l.DemandRef.Version), State = "CLOSED" }, l.DemandRef.Version, ct);
        }
    }
}
