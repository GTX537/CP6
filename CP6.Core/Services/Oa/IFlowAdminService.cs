namespace CP6.Core.Services.Oa;

/// <summary>轻量流程管理项（每表单专属流程 1:1，umbrella §4.8）。</summary>
public record FlowAdminItem(string FlowKey, string FlowName, string FormKey, int Version, bool Enable);

/// <summary>轻量流程管理（填單挂流程前提）。流程列表 / 启停 / 1 表单↔1 启用流程守卫。</summary>
public interface IFlowAdminService
{
    Task<IReadOnlyList<FlowAdminItem>> ListFlowsAsync();
    Task<FlowAdminItem?> GetFlowAsync(string flowKey);
    Task SetEnabledAsync(string flowKey, bool enabled);   // 启用时守 E-WF-008；流程不存在 E-WF-006
}
