using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class CrossDockService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw Error("AUTHORITY_FENCE_UNAVAILABLE", 503);
    private static OrderPriceCorrectionException Error(string code, int status) => new(code, status);
    private Task<T> LegacyRead<T>() => Owner.ExecuteAsync<T>(OperationsOwnerV1.CrossDock, "query", false,
        (_, _) => throw Error("OWNER_READ_VERSION_REQUIRED", 409));
    public Task<List<CrossDockOrder>> SearchAsync(CrossDockSearchQuery query) => !_db.Database.IsNpgsql() ? SearchCoreAsync(query) : LegacyRead<List<CrossDockOrder>>();
    public Task<CrossDockOrder?> GetAsync(string no) => !_db.Database.IsNpgsql() ? GetCoreAsync(no) : LegacyRead<CrossDockOrder?>();
    private Task<T> RejectUnadopted<T>(string action, string? no = null) => Owner.ExecuteAsync<T>(OperationsOwnerV1.CrossDock, action, false, async (a, ct) =>
    {
        if (no != null && !await (await a.CrossDocks(ct)).AnyAsync(x => x.XDockNo == no, ct)) throw Error("OBJECT_NOT_FOUND", 404);
        // Planned does not prove zero prior effects. No numbering, stock call or cancellation before this gate.
        throw Error("CROSS_DOCK_OWNER_ADOPTION_UNPROVEN", 503);
    });
    public Task<string> CreateAsync(CrossDockOrderDto dto, string? userName) => !_db.Database.IsNpgsql() ? CreateCoreAsync(dto, userName) : RejectUnadopted<string>("add");
    public Task ExecuteAsync(string no, string? userName) => !_db.Database.IsNpgsql() ? ExecuteCoreAsync(no, userName) : RejectUnadopted<bool>("execute", no);
    public Task CancelAsync(string no, string? userName) => !_db.Database.IsNpgsql() ? CancelCoreAsync(no, userName) : RejectUnadopted<bool>("cancel", no);

    public Task<CrossDockPageV1> SearchOwnerAsync(CrossDockSearchQuery input)
    {
        var q = OperationsOwnerV1.Freeze(input);
        return Owner.ExecuteAsync(OperationsOwnerV1.CrossDock, "query", false, async (a, ct) =>
        {
            if (q.Page < 1 || q.PageSize < 1 || q.PageSize > 500 || (long)(q.Page - 1) * q.PageSize > int.MaxValue
                || q.Status is not (null or 0 or 1 or 9) || q.DateFrom > q.DateTo
                || new[] { q.XDockNo, q.ProductCd, q.InboundNo, q.OutboundNo }.Any(x => x?.Length > 20)) throw Error("CROSS_DOCK_QUERY_INVALID", 400);
            var rows = await a.CrossDocks(ct);
            if (!string.IsNullOrWhiteSpace(q.XDockNo)) rows = rows.Where(x => x.XDockNo.Contains(q.XDockNo));
            if (!string.IsNullOrWhiteSpace(q.ProductCd)) rows = rows.Where(x => x.ProductCd == q.ProductCd);
            if (!string.IsNullOrWhiteSpace(q.InboundNo)) rows = rows.Where(x => x.InboundNo == q.InboundNo);
            if (!string.IsNullOrWhiteSpace(q.OutboundNo)) rows = rows.Where(x => x.OutboundNo == q.OutboundNo);
            if (q.Status.HasValue) rows = rows.Where(x => x.Status == q.Status);
            if (q.DateFrom.HasValue) rows = rows.Where(x => x.CreateDate >= q.DateFrom);
            if (q.DateTo.HasValue) rows = rows.Where(x => x.CreateDate <= q.DateTo);
            var total = await rows.CountAsync(ct);
            var items = await rows.OrderByDescending(x => x.CreateDate).ThenBy(x => x.Id).Skip((q.Page - 1) * q.PageSize).Take(q.PageSize).ToArrayAsync(ct);
            return new CrossDockPageV1 { Total = total, Page = q.Page, PageSize = q.PageSize, Items = items.Select(a.ProjectCrossDock).ToList() };
        });
    }
    public Task<CrossDockObservationV1?> GetOwnerAsync(string no) => Owner.ExecuteAsync<CrossDockObservationV1?>(OperationsOwnerV1.CrossDock, "query", false, async (a, ct) =>
    {
        if (string.IsNullOrWhiteSpace(no) || no.Length > 20) throw Error("CROSS_DOCK_QUERY_INVALID", 400);
        var row = await (await a.CrossDocks(ct)).SingleOrDefaultAsync(x => x.XDockNo == no, ct);
        return row == null ? null : a.ProjectCrossDock(row);
    });
}
