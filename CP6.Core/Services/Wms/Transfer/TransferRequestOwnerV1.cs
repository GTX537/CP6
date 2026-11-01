using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Core.Services.Space;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using static CP6.Core.Services.Wms.Transfer.TransferEnvironmentStoreV1;
using Store = CP6.Core.Services.Wms.Transfer.TransferRequestStoreV1;

namespace CP6.Core.Services.Wms.Transfer;

public sealed partial class TransferRequestOwnerV1(CP6Context db, OperationsOwnerV1 owner, ScopeAuthorizationFenceV1 scope,
    IOptions<TransferEnvironmentOptionsV1> environment, IOptions<TransferRequestOptionsV1> options,
    QualitySpecificationRuntimeV1 runtime, TransferProductSourcesV1 products, StockMovementService stock)
{
    private OperationsOwnerV1 Authority => owner.Owns(db) ? owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE");
    private TransferEnvironmentStoreV1 EnvironmentStore() => new(db, scope, environment, runtime);
    private static readonly Regex Quantity = new(@"\A(?:0|[1-9][0-9]{0,12})\.[0-9]{8}\z", RegexOptions.NonBacktracking);
    private static string Next(string version) => checked(long.Parse(version, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);
    private static string Amount(string value)
    {
        if (value == null || !Quantity.IsMatch(value) || value == "0.00000000") throw Error("TRANSFER_QUANTITY_INVALID", 422);
        return value;
    }
    private static void Command(Guid operation, string reason)
    {
        if (operation == Guid.Empty || !EngineeringProductValuesV1.ValidText(reason, 1000, true)) throw Error("TRANSFER_COMMAND_INVALID", 400);
    }
    private static TransferRequestLineInputV1[] Inputs(TransferRequestLineInputV1[] lines)
    {
        if (lines == null || lines.Length is < 1 or > 500 || lines.Any(x => x == null)) throw Error("TRANSFER_LINES_INVALID", 400);
        foreach (var l in lines)
        {
            if (!EngineeringProductValuesV1.ValidText(l.ClientLineKey, 100, true)
                || l.SourceWarehouseId == Guid.Empty || l.SourceLocationId == Guid.Empty || l.DestinationWarehouseId == Guid.Empty
                || l.DestinationLocationId == Guid.Empty || l.SourceLocationId == l.DestinationLocationId) throw Error("TRANSFER_LINE_INVALID", 400);
            InventoryIdentityValuesV1.Reference(l.ProductCatalogueRef); UomProfileValuesV1.Reference(l.InputUomRef); Amount(l.InputQuantity);
        }
        if (lines.Select(x => x.ClientLineKey).Distinct(StringComparer.Ordinal).Count() != lines.Length) throw Error("TRANSFER_LINE_KEY_DUPLICATED", 400);
        return lines.OrderBy(x => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(x.ClientLineKey)), StringComparer.Ordinal).ToArray();
    }
    private static void Expected(TransferRequestViewV1 v, string revision, string etag)
    {
        if (string.IsNullOrEmpty(revision) || string.IsNullOrEmpty(etag)) throw Error("TRANSFER_EXPECTED_REQUIRED", 428);
        if (v.Revision != revision || v.Etag != etag) throw Error("TRANSFER_REQUEST_CHANGED", 412);
    }
    private async Task<TransferDeploymentV1> Fence(OperationsAccessV1 a, bool write, CancellationToken ct)
    {
        var deployment = await EnvironmentStore().Fence(a, false, ct);
        await db.Database.ExecuteSqlRawAsync("LOCK TABLE cp6_transfer_request_v1.heads IN " + (write ? "SHARE ROW EXCLUSIVE" : "SHARE") + " MODE", ct);
        return deployment;
    }
    private async Task Visible(OperationsAccessV1 a, TransferRequestViewV1 v, CancellationToken ct)
    {
        await a.RequireTransferRequest(v.Id, ct);
        var ids = v.Lines.SelectMany(x => new[] { x.Input.SourceLocationId, x.Input.DestinationLocationId }).Distinct().ToArray();
        if (await a.TransferLocations().CountAsync(x => ids.Contains(x.Id), ct) != ids.Length) throw Error("TRANSFER_LOCATION_FORBIDDEN", 403);
        a.AtCommit(t => a.RequireTransferRequest(v.Id, t));
    }
    private static TransferRequestViewV1 Project(TransferRequestViewV1 v, Guid actor) => v with
    {
        AllowedActions = v.State switch
        {
            "DRAFT" => ["EDIT", "SUBMIT", "WITHDRAW"],
            "SUBMITTED" when actor != v.AuthorId && actor != v.CreatorId && actor != v.SubmitterId => ["APPROVE", "REJECT", "WITHDRAW"],
            "SUBMITTED" => ["WITHDRAW"], _ => []
        }
    };
    public Task<TransferRequestViewV1> ReadAsync(Guid id, CancellationToken ct = default) => Authority.ExecuteAsync(
        OperationsOwnerV1.TransferRequest, "query", false, async (a, t) =>
        { await Fence(a, false, t); var v = await new Store(db).Read(id, t); await Visible(a, v, t); return Project(v, a.ActorId); }, ct);
    public Task<TransferRequestViewV1[]> ListAsync(int page = 1, CancellationToken ct = default)
    {
        if (page < 1 || page > int.MaxValue / 50) throw Error("TRANSFER_PAGE_INVALID", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferRequest, "query", false, async (a, t) =>
        {
            await Fence(a, false, t);
            var rows = await a.TransferRequests().OrderBy(x => x.Id).Skip((page - 1) * 50).Take(50).ToArrayAsync(t);
            var visible = await a.TransferLocations().Select(x => x.Id).ToArrayAsync(t); var ids = visible.ToHashSet();
            return rows.Select(Store.View).Where(v => v.Lines.All(l => ids.Contains(l.Input.SourceLocationId) && ids.Contains(l.Input.DestinationLocationId)))
                .Select(v => Project(v, a.ActorId)).ToArray();
        }, ct);
    }
    public Task<TransferRequestViewV1> HistoryAsync(Guid id, string revision, CancellationToken ct = default)
    {
        var number = EngineeringProductValuesV1.Revision(revision);
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferRequest, "query", false, async (a, t) =>
        {
            await Fence(a, false, t); await a.RequireTransferRequest(id, t);
            var bytes = await db.Database.SqlQuery<byte[]>($"""
                SELECT body AS "Value" FROM cp6_transfer_request_v1.versions WHERE tenant_id={db.CurrentTenantId} AND id={id} AND revision={number}
                """).SingleOrDefaultAsync(t) ?? throw Error("TRANSFER_REVISION_NOT_FOUND", 404);
            var v = JsonSerializer.Deserialize<TransferRequestViewV1>(bytes, Store.Json)!; await Visible(a, v, t); return v;
        }, ct);
    }
    private Task<TransferRequestResultV1> Execute(Guid operation, string action, object input,
        Func<OperationsAccessV1, TransferEnvironmentRegistrationV1, CancellationToken, Task<(TransferRequestViewV1 View, bool Changed)>> body, CancellationToken ct)
    {
        var bytes = MainOwnerOperationJournalV1.Encode(input);
        if (bytes.Length > 2 * 1024 * 1024) throw Error("TRANSFER_BODY_TOO_LARGE", 413);
        var journal = new MainOwnerOperationJournalV1(db); MainOwnerOperationJournalV1.Original? original = null;
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferRequest, action, true, async (a, t) =>
        {
            await Fence(a, true, t);
            if (original != null) return await Disclose(a, original, bytes, t);
            var registration = await EnvironmentStore().Acquire(a, t);
            var (view, changed) = await body(a, registration, t);
            var result = new TransferRequestResultV1(operation, view, changed);
            if (changed) await new Store(db).Save(view, operation, action, t);
            if (action == "add") await a.BindTransferDepartment(view.Id, view.DepartmentId, t);
            await journal.Store(Store.Owner, Store.Contract, operation, a.ActorId, bytes, result, [view.Id], new { action, view.Id, view.Revision }, t);
            return result;
        }, ct, actionAtFence: async t => { original = await journal.Read(Store.Owner, operation, t); return original == null ? action : "query"; });
    }
    private async Task<TransferRequestResultV1> Disclose(OperationsAccessV1 a, MainOwnerOperationJournalV1.Original original, byte[]? input, CancellationToken ct)
    {
        if (original.Actor != a.ActorId || original.Contract != Store.Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
        if (original.State == "CLOSED") throw Error("ORIGINAL_OPERATION_CLOSED", 409);
        if (input != null && !MainOwnerOperationJournalV1.Matches(original, input)) throw Error("ORIGINAL_OPERATION_INPUT_CONFLICT", 409);
        var result = JsonSerializer.Deserialize<TransferRequestResultV1>(original.Result)!;
        await Visible(a, result.Request, ct); return result;
    }
    public Task<TransferRequestOperationV1> OperationAsync(Guid operation, bool close, CancellationToken ct = default)
    {
        if (operation == Guid.Empty) throw Error("ORIGINAL_OPERATION_REQUIRED", 400);
        return Authority.ExecuteAsync(OperationsOwnerV1.TransferRequest, "query", close, async (a, t) =>
        {
            await Fence(a, close, t); var journal = new MainOwnerOperationJournalV1(db); var original = await journal.Read(Store.Owner, operation, t);
            if (original != null)
            {
                if (original.Actor != a.ActorId || original.Contract != Store.Contract) throw Error("ORIGINAL_OPERATION_FORBIDDEN", 403);
                return original.State == "CLOSED" ? new(operation, "CLOSED") : new TransferRequestOperationV1(operation, "COMMITTED", await Disclose(a, original, null, t));
            }
            if (!close) return new TransferRequestOperationV1(operation, "UNKNOWN");
            var result = new TransferRequestOperationV1(operation, "CLOSED");
            await journal.Store(Store.Owner, Store.Contract, operation, a.ActorId, null, result, [], new { action = "close-uncommitted-transfer-request" }, t); return result;
        }, ct, readOnlyAuthorityFence: !close);
    }
}
