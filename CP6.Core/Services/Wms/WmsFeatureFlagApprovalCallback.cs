using CP6.Core.Services.Wf;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Services.Wms;

public sealed class WmsFeatureFlagApprovalCallback : IApprovalCallback
{
    private readonly IServiceProvider _services;

    public WmsFeatureFlagApprovalCallback(IServiceProvider services)
        => _services = services;

    public string BizType => WmsFeatureFlagChangeService.ApprovalBizType;

    public Task OnApprovedAsync(ApprovalCallbackContext context)
        => _services.GetRequiredService<IWmsFeatureFlagChangeService>()
            .ApplyApprovedAsync(ParseId(context.BizId), context);

    public Task OnRejectedAsync(ApprovalCallbackContext context)
        => _services.GetRequiredService<IWmsFeatureFlagChangeService>()
            .ApplyRejectedAsync(ParseId(context.BizId), context);

    private static Guid ParseId(string value)
        => Guid.TryParse(value, out var id)
            ? id
            : throw new WmsFeatureFlagChangeException("WM-FEATURE-CHANGE-ID-INVALID");
}
