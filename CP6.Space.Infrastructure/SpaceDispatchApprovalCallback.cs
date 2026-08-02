using CP6.Core.Services.Wf;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Space.Infrastructure;

public sealed class SpaceDispatchApprovalCallback(IServiceProvider services)
    : IApprovalCallback
{
    public string BizType => SpaceDispatchApprovalService.ApprovalBizType;

    public Task OnApprovedAsync(ApprovalCallbackContext context) =>
        services.GetRequiredService<SpaceDispatchApprovalService>()
            .ApplyApprovedAsync(ParseId(context.BizId), context);

    public Task OnRejectedAsync(ApprovalCallbackContext context) =>
        services.GetRequiredService<SpaceDispatchApprovalService>()
            .ApplyRejectedAsync(ParseId(context.BizId), context);

    private static Guid ParseId(string value) =>
        Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException(
                "SPACE_DISPATCH_APPROVAL_ID_INVALID");
}
