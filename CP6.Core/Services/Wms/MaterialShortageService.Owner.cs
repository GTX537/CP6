using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Mes;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class MaterialShortageService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static OrderPriceCorrectionException Error(string code, int status) => new(code, status);

    public Task<MaterialShortage> CreateAsync(MaterialShortage entity) => !_db.Database.IsNpgsql() ? CreateCoreAsync(entity) :
        throw Error("INTERNAL_SHORTAGE_CAPABILITY_REQUIRED", 403);
    internal async Task<MaterialShortage> CreateWithinOwnerAsync(MaterialShortage entity, OperationsAccessV1 access, CancellationToken ct)
    {
        await access.RequireShortageCreation(_db, entity, ct);
        entity.TenantId = _db.CurrentTenantId; entity.Creator = access.Actor;
        return await CreateCoreAsync(entity);
    }
    public Task<PagedResultDto<MaterialShortage>> SearchAsync(MaterialShortageQuery query) => !_db.Database.IsNpgsql() ? SearchCoreAsync(query) :
        Owner.ExecuteAsync<PagedResultDto<MaterialShortage>>(OperationsOwnerV1.MaterialShortage, "query", false,
            (_, _) => throw Error("OWNER_READ_VERSION_REQUIRED", 409));
    public Task<MaterialShortage> ResolveAsync(Guid id, string? remark, string? userName) => !_db.Database.IsNpgsql() ? ResolveCoreAsync(id, remark, userName) :
        Owner.ExecuteAsync<MaterialShortage>(OperationsOwnerV1.MaterialShortage, "resolve", true, (_, _) => throw Error("ROW_VERSION_REQUIRED", 409));
    public Task<MaterialShortage> DismissAsync(Guid id, string? remark, string? userName) => !_db.Database.IsNpgsql() ? DismissCoreAsync(id, remark, userName) :
        Owner.ExecuteAsync<MaterialShortage>(OperationsOwnerV1.MaterialShortage, "dismiss", true, (_, _) => throw Error("ROW_VERSION_REQUIRED", 409));

    private static MaterialShortageRowV1 Project(MaterialShortage row, bool remarkReadable)
    {
        var required = InventoryReportValuesV1.Exact(row.RequiredQty); var available = InventoryReportValuesV1.Exact(row.AvailableQty);
        var shortage = required.Subtract(available); var zero = InventoryReportValuesV1.Exact(0);
        return new() { Id = row.Id, RowVersion = row.RowVersion?.ToArray(), WorkOrderNo = row.WorkOrderNo,
            RelatedOutboundNo = row.RelatedOutboundNo, ProductCd = row.ProductCd, LotNo = row.LotNo,
            RequiredQty = required.ToString(), AvailableQty = available.ToString(), ShortQty = shortage.CompareTo(zero) > 0 ? shortage.ToString() : "0",
            DetectedAt = row.DetectedAt, ResolvedAt = row.ResolvedAt, Status = row.Status, Remark = remarkReadable ? row.Remark : null };
    }
    public Task<MaterialShortagePageV1> SearchOwnerAsync(MaterialShortageQuery input)
    {
        var query = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.MaterialShortage, "query", false, async (a, ct) =>
        {
            if (query.Page < 1 || query.PageSize is < 1 or > 500 || (long)(query.Page - 1) * query.PageSize > int.MaxValue
                || query.WorkOrderNo?.Length > 20 || query.Status is not (null or "" or MaterialShortageStatus.Open or MaterialShortageStatus.Resolved or MaterialShortageStatus.Dismissed))
                throw Error("SHORTAGE_QUERY_INVALID", 400);
            var rows = await a.MaterialShortages(ct);
            if (!string.IsNullOrWhiteSpace(query.WorkOrderNo)) rows = rows.Where(s => s.WorkOrderNo == query.WorkOrderNo);
            var open = await rows.CountAsync(s => s.Status == MaterialShortageStatus.Open, ct);
            if (!string.IsNullOrEmpty(query.Status)) rows = rows.Where(s => s.Status == query.Status);
            var total = await rows.CountAsync(ct);
            var page = await rows.OrderByDescending(s => s.DetectedAt).ThenBy(s => s.Id)
                .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArrayAsync(ct);
            return new MaterialShortagePageV1 { Total = total, OpenCount = open, Page = query.Page, PageSize = query.PageSize,
                RemarkReadable = a.ShortageRemarkReadable, RemarkWritable = a.ShortageRemarkWritable,
                Rows = page.Select(s => Project(s, a.ShortageRemarkReadable)).ToList() };
        });
    }

    public Task<MaterialShortageRowV1> ResolveOwnerAsync(Guid id, MaterialShortageActionV1 input) => TransitionOwnerAsync(id, input, "resolve", MaterialShortageStatus.Resolved);
    public Task<MaterialShortageRowV1> DismissOwnerAsync(Guid id, MaterialShortageActionV1 input) => TransitionOwnerAsync(id, input, "dismiss", MaterialShortageStatus.Dismissed);
    private Task<MaterialShortageRowV1> TransitionOwnerAsync(Guid id, MaterialShortageActionV1 input, string action, string status)
    {
        var command = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.MaterialShortage, action, true, async (a, ct) =>
        {
            await a.RequireMaterialShortage(id, ct);
            if (command.RowVersion is not { Length: > 0 }) throw Error("ROW_VERSION_REQUIRED", 409);
            if (command.Remark?.Length > 500 || !command.SetRemark && command.Remark != null) throw Error("SHORTAGE_COMMAND_INVALID", 400);
            if (command.SetRemark && !a.ShortageRemarkWritable) throw Error("FIELD_WRITE_FORBIDDEN", 403);
            var row = await _db.MaterialShortages.SingleAsync(s => s.Id == id, ct);
            if (row.RowVersion == null || !command.RowVersion.SequenceEqual(row.RowVersion)) throw Error("ROW_VERSION_CONFLICT", 409);
            if (row.Status != MaterialShortageStatus.Open) throw Error("WM-MSG-SHORTAGE-409", 409);
            row.Status = status; if (command.SetRemark) row.Remark = command.Remark;
            row.ResolvedAt = DateTime.UtcNow; row.Modifier = a.Actor; row.ModifyDate = DateTime.Now;
            await _db.SaveChangesAsync(ct); a.AtCommit(t => a.RequireMaterialShortage(id, t));
            return Project(row, a.ShortageRemarkReadable);
        });
    }
}
