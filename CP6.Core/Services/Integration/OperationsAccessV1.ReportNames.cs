using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Integration;

internal sealed partial class OperationsAccessV1
{
    // 名称是可隐藏的关联显示值。明确缺少来源 query 时只隐藏名称；
    // LinkedSource 仅处理该拒绝，来源不可用等错误继续向上返回。
    internal async Task<Dictionary<string, string>> WarehouseNamesAsync(string[] codes, CancellationToken ct)
    {
        var source = await LinkedSource(OperationsOwnerV1.Warehouse, ct);
        if (source == null || !source.MasterRead("WarehouseName")) return [];
        return await source.MasterWarehouses().Where(w => codes.Contains(w.WarehouseCd))
            .ToDictionaryAsync(w => w.WarehouseCd, w => w.WarehouseName, ct);
    }

    internal async Task<(bool Readable, Dictionary<string, string> Values)> CustomerNamesAsync(string[] codes, CancellationToken ct)
    {
        var source = await LinkedSource(OperationsOwnerV1.BusinessPartner, ct);
        if (source == null || !source.BusinessPartnerNameReadable) return (false, []);
        var values = await source.BusinessPartners().Where(p => codes.Contains(p.BpCd))
            .ToDictionaryAsync(p => p.BpCd, p => p.BpName, ct);
        return (true, values);
    }
}
