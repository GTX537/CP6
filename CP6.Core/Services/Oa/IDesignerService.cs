namespace CP6.Core.Services.Oa;

/// <summary>流程设计器服务（umbrella §4.8）。校验 + 身份唯一 + upsert（消费 IFlowDefService）+ 模板克隆。</summary>
public interface IDesignerService
{
    Task<IReadOnlyList<FlowDefSummary>> ListAsync(string? functionId = null);  // functionId 非空=按功能筛
    Task<FlowDefSummary?> LoadAsync(string flowKey);                            // 取定义摘要（schema 经 GetDef 取）
    Task SaveAsync(SaveFlowRequest req, string? user);                          // 校验 E-WF-010 + 唯一 E-WF-009 + SaveDef
    Task CloneAsync(CloneRequest req, string? user);                           // 独立副本
}
