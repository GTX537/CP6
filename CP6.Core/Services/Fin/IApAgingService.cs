namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付账龄（章03 §5）。按到期日相对基准日分桶（未到期 / 逾期 1-30 / 31-60 / 60+），
/// 金额折本位币、按供应商汇总；红字反向。逾期桶可驱动 SignalR 预警（控制器层）。
/// </summary>
public interface IApAgingService
{
    /// <summary>取账龄（按供应商分组）。asOf=账龄基准日；supplierId 可选过滤。</summary>
    Task<List<ApAgingRow>> AgingAsync(DateTime asOf, string? supplierId = null);
}

/// <summary>账龄一行（按供应商，金额本位币）。</summary>
public class ApAgingRow
{
    public string SupplierId { get; set; } = string.Empty;

    /// <summary>未到期（到期日 ≥ 基准日）</summary>
    public decimal NotDue { get; set; }

    /// <summary>逾期 1-30 天</summary>
    public decimal Days1To30 { get; set; }

    /// <summary>逾期 31-60 天</summary>
    public decimal Days31To60 { get; set; }

    /// <summary>逾期 60 天以上</summary>
    public decimal Days60Plus { get; set; }

    /// <summary>未付合计</summary>
    public decimal Total => NotDue + Days1To30 + Days31To60 + Days60Plus;

    /// <summary>逾期合计（1-30 + 31-60 + 60+），驱动预警</summary>
    public decimal Overdue => Days1To30 + Days31To60 + Days60Plus;
}
