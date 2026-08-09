namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 审批委托契约（采购侧端口，P-D1）。采购送审 PR/PO 时经本契约请审批中台裁决，
/// 编译期只依赖抽象——OA 审批引擎（阶段1~4 已落地）后续以适配器接真实流程绑定，
/// MVP 先用 <see cref="StubApprovalService"/> 单人/跳过桩，保证 PO→GR→匹配→AP 主链可独立交付。
/// </summary>
public interface IApprovalService
{
    /// <summary>提交审批。返回是否即时通过（桩=true）及审批引用号。</summary>
    Task<ApprovalSubmitResult> SubmitAsync(ApprovalSubmitRequest request);
}

/// <summary>送审请求。</summary>
public class ApprovalSubmitRequest
{
    /// <summary>业务类型（如 "PUR_PO"），对应 OA 审批绑定。</summary>
    public string BizType { get; set; } = string.Empty;

    /// <summary>业务单据键（如 PoNo）。</summary>
    public string BizKey { get; set; } = string.Empty;

    /// <summary>Authenticated business actor. Never inferred to Guid.Empty.</summary>
    public Guid ActorId { get; set; }

    /// <summary>Server-built, trusted business snapshot used for routing and runtime variables.</summary>
    public object Snapshot { get; set; } = new();

    /// <summary>Business-reserved workflow id used to correlate ApprovalRef before an immediate callback.</summary>
    public Guid? InstanceId { get; set; }

    /// <summary>送审金额（审批路由可按金额分级）。</summary>
    public decimal Amount { get; set; }

    /// <summary>提交人。</summary>
    public string? Submitter { get; set; }
}

/// <summary>送审结果。</summary>
public class ApprovalSubmitResult
{
    /// <summary>是否即时通过（无需流转，桩/自动放行=true）。</summary>
    public bool AutoApproved { get; set; }

    /// <summary>审批引用号（回填单据 ApprovalRef）。</summary>
    public string ApprovalRef { get; set; } = string.Empty;
}
