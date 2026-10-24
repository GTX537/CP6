using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class KittingService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static OrderPriceCorrectionException Error(string code, int status) => new(code, status);
    private Task<T> LegacyRead<T>() => Owner.ExecuteAsync<T>(OperationsOwnerV1.Kitting, "query", false,
        (_, _) => throw Error("OWNER_READ_VERSION_REQUIRED", 409));
    private Task<T> RejectWrite<T>(string action, string? master = null, string? order = null) => Owner.ExecuteAsync<T>(OperationsOwnerV1.Kitting, action, false, async (a, ct) =>
    {
        if (master != null && !await a.KitMasters().AnyAsync(x => x.KitSku == master, ct)
            || order != null && !await a.KitOrders().AnyAsync(x => x.KitOrderNo == order, ct)) throw Error("OBJECT_NOT_FOUND", 404);
        throw Error("KIT_OWNER_ADOPTION_UNPROVEN", 503);
    });
    public Task<List<KitMaster>> SearchMastersAsync(string? keyword) => !_db.Database.IsNpgsql() ? SearchMastersCoreAsync(keyword) : LegacyRead<List<KitMaster>>();
    public Task<KitMasterDto?> GetMasterAsync(string sku) => !_db.Database.IsNpgsql() ? GetMasterCoreAsync(sku) : LegacyRead<KitMasterDto?>();
    public Task<List<KitOrder>> SearchOrdersAsync(KitOrderSearchQuery q) => !_db.Database.IsNpgsql() ? SearchOrdersCoreAsync(q) : LegacyRead<List<KitOrder>>();
    public Task<KitOrder?> GetOrderAsync(string no) => !_db.Database.IsNpgsql() ? GetOrderCoreAsync(no) : LegacyRead<KitOrder?>();
    public Task CreateMasterAsync(KitMasterDto dto, string? userName) => !_db.Database.IsNpgsql() ? CreateMasterCoreAsync(dto, userName) : RejectWrite<bool>("add");
    public Task UpdateMasterAsync(string sku, KitMasterDto dto, string? userName) => !_db.Database.IsNpgsql() ? UpdateMasterCoreAsync(sku, dto, userName) : RejectWrite<bool>("edit", master: sku);
    public Task DeleteMasterAsync(string sku, string? userName) => !_db.Database.IsNpgsql() ? DeleteMasterCoreAsync(sku, userName) : RejectWrite<bool>("del", master: sku);
    public Task<string> CreateOrderAsync(KitOrderDto dto, string? userName) => !_db.Database.IsNpgsql() ? CreateOrderCoreAsync(dto, userName) : RejectWrite<string>("add");
    public Task ExecuteAsync(string no, string? userName) => !_db.Database.IsNpgsql() ? ExecuteCoreAsync(no, userName) : RejectWrite<bool>("execute", order: no);
    public Task CancelAsync(string no, string? userName) => !_db.Database.IsNpgsql() ? CancelCoreAsync(no, userName) : RejectWrite<bool>("cancel", order: no);
    private static void Validate(KitQueryV1 q, bool master)
    {
        if (q.Page < 1 || q.PageSize is < 1 or > 200 || (long)(q.Page - 1) * q.PageSize > int.MaxValue
            || q.Keyword?.Length > 100 || q.WarehouseCd?.Length > 10 || q.Status is not (null or 0 or 1 or 9)
            || q.Direction is not (null or "ASSEMBLE" or "DISASSEMBLE")
            || master && (q.Status != null || q.Direction != null) || !master && q.Active != null) throw Error("KIT_QUERY_INVALID", 400);
    }
    public Task<KitPageV1<KitMasterObservationV1>> SearchMastersOwnerAsync(KitQueryV1 input)
    {
        var q = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.Kitting, "query", false, async (a, ct) =>
        {
            Validate(q, true); var rows = a.KitMasters();
            if (!string.IsNullOrWhiteSpace(q.Keyword)) rows = rows.Where(x => x.KitSku.Contains(q.Keyword));
            if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) rows = rows.Where(x => x.DefaultWarehouseCd == q.WarehouseCd);
            if (q.Active.HasValue) rows = rows.Where(x => x.ActiveFlg == q.Active);
            var total = await rows.CountAsync(ct);
            var items = await rows.OrderBy(x => x.KitSku).ThenBy(x => x.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            return new KitPageV1<KitMasterObservationV1> { Total = total, Page = q.Page, PageSize = q.PageSize, Items = items.Select(a.ProjectKitMaster).ToList() };
        });
    }
    public Task<KitMasterDetailV1?> GetMasterOwnerAsync(string sku) => Owner.ExecuteAsync<KitMasterDetailV1?>(OperationsOwnerV1.Kitting, "query", false, async (a, ct) =>
    {
        if (string.IsNullOrWhiteSpace(sku) || sku.Length > 20) throw Error("KIT_QUERY_INVALID", 400);
        var row = await a.KitMasters().SingleOrDefaultAsync(x => x.KitSku == sku, ct); if (row == null) return null;
        var components = await _db.KitMasterComponents.AsNoTracking().Where(x => !x.IsDeleted && x.KitSku == row.KitSku)
            .OrderBy(x => x.LineNo).ThenBy(x => x.Id).ToArrayAsync(ct);
        return new(a.ProjectKitMaster(row), components.Select(a.ProjectKitComponent).ToList());
    });
    public Task<KitPageV1<KitOrderObservationV1>> SearchOrdersOwnerAsync(KitQueryV1 input)
    {
        var q = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.Kitting, "query", false, async (a, ct) =>
        {
            Validate(q, false); var rows = a.KitOrders();
            if (!string.IsNullOrWhiteSpace(q.Keyword)) rows = rows.Where(x => x.KitOrderNo.Contains(q.Keyword) || x.KitSku.Contains(q.Keyword));
            if (!string.IsNullOrWhiteSpace(q.WarehouseCd)) rows = rows.Where(x => x.WarehouseCd == q.WarehouseCd);
            if (q.Status.HasValue) rows = rows.Where(x => x.Status == q.Status);
            if (q.Direction != null) rows = rows.Where(x => x.Direction == q.Direction);
            var total = await rows.CountAsync(ct);
            var items = await rows.OrderByDescending(x => x.CreateDate).ThenBy(x => x.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            var projected = new List<KitOrderObservationV1>(); foreach (var x in items) projected.Add(await a.ProjectKitOrder(x, ct));
            return new KitPageV1<KitOrderObservationV1> { Total = total, Page = q.Page, PageSize = q.PageSize, Items = projected };
        });
    }
    public Task<KitOrderObservationV1?> GetOrderOwnerAsync(string no) => Owner.ExecuteAsync<KitOrderObservationV1?>(OperationsOwnerV1.Kitting, "query", false, async (a, ct) =>
    {
        if (string.IsNullOrWhiteSpace(no) || no.Length > 20) throw Error("KIT_QUERY_INVALID", 400);
        var row = await a.KitOrders().SingleOrDefaultAsync(x => x.KitOrderNo == no, ct);
        return row == null ? null : await a.ProjectKitOrder(row, ct);
    });
}
