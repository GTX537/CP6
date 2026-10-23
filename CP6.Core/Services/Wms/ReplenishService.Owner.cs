using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class ReplenishService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner
        : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);

    public Task<ReplenishCapabilitiesV1> GetCapabilitiesAsync() => !_db.Database.IsNpgsql()
        ? Task.FromResult(new ReplenishCapabilitiesV1(true, "LEGACY"))
        : Owner.ExecuteAsync(OperationsOwnerV1.Replenish, "query", false, (_, _) =>
            Task.FromResult(new ReplenishCapabilitiesV1(false, "REPLENISH_OWNER_ADOPTION_UNPROVEN")));

    public Task<List<ReplenishOrder>> SearchAsync(ReplenishSearchQuery query)
    {
        if (!_db.Database.IsNpgsql()) return SearchCoreAsync(query);
        var frozen = OperationsOwnerV1.Freeze(query);
        frozen.PageSize = Math.Clamp(frozen.PageSize, 1, 500);
        frozen.Page = Math.Clamp(frozen.Page, 1, int.MaxValue / frozen.PageSize);
        return Owner.ExecuteAsync(OperationsOwnerV1.Replenish, "query", false, async (access, ct) =>
        {
            var rows = await SearchCoreAsync(frozen, access.Replenishments(), ct);
            foreach (var row in rows) access.ProjectReplenishment(row, list: true);
            return rows;
        });
    }

    public Task<ReplenishOrder?> GetAsync(string number) => !_db.Database.IsNpgsql() ? GetCoreAsync(number)
        : Owner.ExecuteAsync(OperationsOwnerV1.Replenish, "query", false, async (access, ct) =>
        {
            var row = await GetCoreAsync(number, access.Replenishments(), ct);
            if (row is not null) access.ProjectReplenishment(row, list: false);
            return row;
        });

    // The old commands cannot establish the accepted rule/cut, target demand budget,
    // Transfer adoption or original task-family closure. Do not fall back to that chain.
    private Task<T> RejectUnadopted<T>(string action, string? number = null) =>
        Owner.ExecuteAsync<T>(OperationsOwnerV1.Replenish, action, false, async (access, ct) =>
        {
            if (number is not null) await access.RequireReplenishment(number, ct);
            throw new OrderPriceCorrectionException("REPLENISH_OWNER_ADOPTION_UNPROVEN", 503);
        });

    public Task<string> CreateAsync(ReplenishOrderDto dto, string? userName) => !_db.Database.IsNpgsql()
        ? CreateCoreAsync(dto, userName) : RejectUnadopted<string>("add");
    public Task UpdateAsync(string number, ReplenishOrderDto dto, string? userName) => !_db.Database.IsNpgsql()
        ? UpdateCoreAsync(number, dto, userName) : RejectUnadopted<bool>("add", number);
    public Task<int> GenerateBatchAsync(string warehouseCd, decimal minQty, string? userName) => !_db.Database.IsNpgsql()
        ? GenerateBatchCoreAsync(warehouseCd, minQty, userName) : RejectUnadopted<int>("generate");
    public Task<string> ExecuteAsync(string number, string? userName) => !_db.Database.IsNpgsql()
        ? ExecuteCoreAsync(number, userName) : RejectUnadopted<string>("execute", number);
    public Task CancelAsync(string number, string? userName) => !_db.Database.IsNpgsql()
        ? CancelCoreAsync(number, userName) : RejectUnadopted<bool>("cancel", number);
}
