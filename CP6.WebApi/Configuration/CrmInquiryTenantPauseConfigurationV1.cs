using CP6.Core.Services.Platform;

namespace CP6.WebApi.Configuration;

internal static class CrmInquiryTenantPauseConfigurationV1
{
    internal static void Register(IServiceCollection services, IConfiguration configuration)
    {
        var bindings = new Dictionary<Guid, CrmInquiryTenantPauseBindingV1>();
        foreach (var tenant in configuration.GetSection("CrmInquiryTenantPause:Tenants").GetChildren())
        {
            if (!Guid.TryParseExact(tenant.Key, "D", out var id) ||
                tenant.Get<CrmInquiryTenantPauseBindingV1>() is not { } binding ||
                !bindings.TryAdd(id, binding))
                throw new InvalidOperationException("CRM_TENANT_PAUSE_BINDING_INVALID");
        }
        if (bindings.Count > 0)
            services.AddSingleton(_ => new CrmInquiryTenantPauseV1(bindings));
    }
}
