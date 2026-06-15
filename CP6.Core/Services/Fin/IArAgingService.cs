namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收账龄（章04 §3，镜像 <see cref="IApAgingService"/>）。按到期日相对基准日分桶（未到期 / 逾期 1-30 / 31-60 / 60+），
/// 金额折本位币、按客户汇总；红字反向。逾期桶可驱动催收预警（控制器层）。
/// </summary>
public interface IArAgingService
{
    /// <summary>取账龄（按客户分组）。asOf=账龄基准日；customerId 可选过滤。</summary>
    Task<List<ArAgingRow>> AgingAsync(DateTime asOf, string? customerId = null);
}

/// <summary>账龄一行（按客户，金额本位币）。</summary>
public class ArAgingRow
{
    public string CustomerId { get; set; } = string.Empty;

    /// <summary>未到期（到期日 ≥ 基准日）</summary>
    public decimal NotDue { get; set; }

    /// <summary>逾期 1-30 天</summary>
    public decimal Days1To30 { get; set; }

    /// <summary>逾期 31-60 天</summary>
    public decimal Days31To60 { get; set; }

    /// <summary>逾期 60 天以上</summary>
    public decimal Days60Plus { get; set; }

    /// <summary>未收合计</summary>
    public decimal Total => NotDue + Days1To30 + Days31To60 + Days60Plus;

    /// <summary>逾期合计（1-30 + 31-60 + 60+），驱动催收预警</summary>
    public decimal Overdue => Days1To30 + Days31To60 + Days60Plus;
}
