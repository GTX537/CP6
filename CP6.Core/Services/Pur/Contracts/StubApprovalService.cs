namespace CP6.Core.Services.Pur.Contracts;

/// <summary>
/// 审批桩（P-D1，MVP）。单人/跳过：任何送审即时通过，返回 "AUTO-{BizKey}" 引用。
/// OA 审批引擎接真实流程绑定后，以适配器实现 <see cref="IApprovalService"/> 替换本桩（DI 切换）。
/// </summary>
public class StubApprovalService : IApprovalService
{
    /// <inheritdoc />
    public Task<ApprovalSubmitResult> SubmitAsync(ApprovalSubmitRequest request)
        => Task.FromResult(new ApprovalSubmitResult
        {
            AutoApproved = true,
            ApprovalRef = $"AUTO-{request.BizKey}",
        });
}
