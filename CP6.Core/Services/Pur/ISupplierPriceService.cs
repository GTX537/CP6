using CP6.Entity.DomainModels.Pur;

namespace CP6.Core.Services.Pur;

/// <summary>
/// 采购价表服务（采购 章01 §3/§4）。维护供应商×物料阶梯价 + 建 PO 时带价解析。
/// </summary>
public interface ISupplierPriceService
{
    /// <summary>
    /// 解析采购单价：取"满足 MinQty≤<paramref name="qty"/> ∧ <paramref name="onDate"/> 落在 [ValidFrom,ValidTo] 有效期内"
    /// 的价表中 MinQty 最大的一档单价。无适用价返回 null（由调用方决定手填或挡单）。
    /// </summary>
    Task<decimal?> ResolvePriceAsync(string supplierId, string itemId, decimal qty, DateTime onDate);

    /// <summary>列出某供应商（可再按物料过滤）的价表。</summary>
    Task<List<SupplierPrice>> ListAsync(string supplierId, string? itemId = null);

    /// <summary>新增/更新一档价。</summary>
    Task<SupplierPrice> SaveAsync(SupplierPrice price, string? userName);

    /// <summary>删除一档价（软删）。</summary>
    Task DeleteAsync(Guid id, string? userName);
}
