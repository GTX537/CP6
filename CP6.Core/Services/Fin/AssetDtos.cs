using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>折旧试算/明细展示 DTO（PreviewAsync 返回，spec §3.2）。</summary>
public sealed class DepreciationEntryDto
{
    public Guid AssetCardId { get; set; }
    public string AssetNo { get; set; } = string.Empty;
    public string AssetName { get; set; } = string.Empty;
    public DepreciationMethod Method { get; set; }
    public decimal DepreciationAmount { get; set; }
    public decimal OpeningAccumulated { get; set; }
    public decimal ClosingAccumulated { get; set; }
    public Guid DeprecExpenseAccountId { get; set; }
    public Guid AccumDeprecAccountId { get; set; }
    public Guid? CostCenterId { get; set; }
    public decimal? WorkloadThisPeriod { get; set; }
}

/// <summary>单卡前瞻折旧计划行（GetScheduleAsync，spec §3.2）。</summary>
public sealed class DepreciationScheduleRow
{
    public int PeriodIndex { get; set; }
    public string YearMonth { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Accumulated { get; set; }
    public decimal NetValue { get; set; }
}

/// <summary>处置月补提结果（AccrueDisposalFinalAsync，供处置 ConfirmAsync 调用，spec §4.3）。</summary>
public sealed class DisposalFinalResult
{
    public bool Ok { get; set; }
    public string? Code { get; set; }
    public bool Skipped { get; set; }
    public Guid? RunId { get; set; }
    public Guid? DeprecEntryId { get; set; }
    public decimal Amount { get; set; }
}
