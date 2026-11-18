namespace CP6.WebApi.Services;

/// <summary>账户与询盘分别登记。即使属于同一租户，两个用途也不能共享客户端或稳定主体。</summary>
public sealed class AccountC03ServicePrincipalRegistryV1 : ServicePrincipalRegistryV1
{
    public AccountC03ServicePrincipalRegistryV1(InquiryServicePrincipalOptionsV1 account, CrmOidcOptions oidc,
        InquiryServicePrincipalRegistryV1 inquiry) : base(account, oidc)
    {
        if (Enabled && !IdentityPublicationEnabled)
            throw new InvalidOperationException("ACCOUNT_C03_IDENTITY_PUBLICATION_REQUIRED");
        if (Sources.Any(a => inquiry.Sources.Any(i => a.Subject == i.Subject || a.Client == i.Client)))
            throw new InvalidOperationException("ACCOUNT_C03_IDENTITY_REGISTRATION_COLLISION");
    }
}
