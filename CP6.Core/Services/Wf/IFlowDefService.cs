using CP6.Entity.DomainModels.Wf;

namespace CP6.Core.Services.Wf;

/// <summary>流程定义 CRUD + 实例详情查询（OA 章03/04）。执行态在 IFlowEngine，这里只管定义与查询。</summary>
public interface IFlowDefService
{
    /// <summary>建/改流程定义（按 FlowKey upsert，schema 变更自增 Version）。返回 FlowDef.Id。</summary>
    Task<Guid> SaveDefAsync(string flowKey, string flowName, string formKey, string schemaJson, string? user = null);

    /// <summary>取流程定义。</summary>
    Task<Wf_FlowDef?> GetDefAsync(string flowKey);

    /// <summary>取实例详情：实例 + 审批痕迹时间线 + 任务列表。不存在返 null。</summary>
    Task<FlowInstanceDetail?> GetInstanceDetailAsync(Guid instanceId);
}

/// <summary>流程实例详情（实例 + 痕迹 + 任务）。前端"我的申请/审批痕迹"用。</summary>
public class FlowInstanceDetail
{
    public Wf_FlowInstance Instance { get; set; } = default!;
    public List<Wf_FlowHistory> History { get; set; } = new();
    public List<Wf_FlowTask> Tasks { get; set; } = new();
}
