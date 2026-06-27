namespace CP6.Core.Services.Integration;

/// <summary>
/// WMS 库位库存量查询契约（停用前置校验 D6 用）。
/// P1 用 <see cref="StubWmsStockQuery"/> 桩；真实查询属 WMS 模块（ch07）。
/// </summary>
public interface IWmsStockQuery
{
    /// <summary>返回该库位当前库存量。</summary>
    Task<int> GetStockQtyAsync(string locationCode);
}

/// <summary>P1 桩：恒返回 0（无库存）。真实查询属 WMS 模块（ch07）。</summary>
public sealed class StubWmsStockQuery : IWmsStockQuery
{
    public Task<int> GetStockQtyAsync(string locationCode) => Task.FromResult(0);
}
