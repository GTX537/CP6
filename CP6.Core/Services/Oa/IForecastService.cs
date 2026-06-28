namespace CP6.Core.Services.Oa;

/// <summary>预计流程前推（umbrella §4.3）。FormDetail 预计段 + FormInitiate 提交前预览共用。</summary>
public interface IForecastService
{
    /// <param name="fromNodeId">null=从起点前推（发起预览）；非 null=从该当前关卡的下一步前推（详情预计段）。</param>
    Task<ForecastResult> ForecastAsync(string flowKey, string varsJson, Guid starterId, string? fromNodeId = null);
}
