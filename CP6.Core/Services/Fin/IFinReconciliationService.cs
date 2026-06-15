namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务每日对账（章10 §5 / 09 数据一致性）。跑多项一致性勾稽，任一不符→issue。
/// 复用 TrialBalance（试算平衡）+ ReconcileAp/Ar（子账↔GL 控制科目）。供 FinReconciliationWorker 每日调度。
/// </summary>
public interface IFinReconciliationService
{
    /// <summary>跑全部对账项，返回不一致清单（空=全部一致）。</summary>
    Task<FinReconciliationResult> ReconcileAsync();
}

/// <summary>对账一项不一致。</summary>
public record FinReconcileIssue(string Category, string Detail);

/// <summary>对账结果。</summary>
public class FinReconciliationResult
{
    public List<FinReconcileIssue> Issues { get; init; } = new();
    /// <summary>全部一致（无 issue）。</summary>
    public bool AllClear => Issues.Count == 0;
}
