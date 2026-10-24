using System.Text;
using CP6.WebApi.Services;
using Microsoft.Extensions.Primitives;

namespace CP6.Tests.Platform;

public sealed class InquiryServicePrincipalRegistrationTests
{
    private static (InquiryServicePrincipalOptionsV1 Source, CrmOidcOptions Oidc) Options()
    {
        var tenant = Guid.NewGuid();
        return (new() { Enabled = true, Version = "owned-configuration-v1", Registrations = [new() {
                ClientId = "worker", SubjectId = Guid.NewGuid(), DepartmentId = Guid.NewGuid(), RoleIds = [int.MinValue, 0, int.MaxValue] }] },
            new() { Enabled = true, Organizations = [new() { TenantId = tenant, CrmEnabled = true }],
                ServiceClients = [new() { ClientId = "worker", Enabled = true, TenantId = tenant,
                    SecretSha256 = CrmOidcCrypto.Hash("owned-secret"), AllowedScopes = ["cp6.services"] }] });
    }
    private static StringValues Basic(string secret = "owned-secret") => "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("worker:" + secret));

    [Fact]
    public void Unconfigured_service_does_not_gain_a_principal_or_reuse_token_scope()
    {
        Assert.False(new InquiryServicePrincipalRegistryV1(new(), new()).Enabled);
        var (source, oidc) = Options(); oidc.ServiceClients[0].Enabled = false;
        Assert.Null(new InquiryServicePrincipalRegistryV1(source, oidc).Authenticate(Basic()));
    }

    [Fact]
    public void Registration_is_frozen_and_new_host_is_required_for_identity_membership_or_secret_rotation()
    {
        var (source, oidc) = Options(); var registry = new InquiryServicePrincipalRegistryV1(source, oidc);
        var first = registry.Authenticate(Basic())!; Assert.Equal(new[] { int.MinValue, 0, int.MaxValue }, first.Roles);
        source.Registrations[0].RoleIds[0] = 2; source.Registrations[0].SubjectId = Guid.NewGuid();
        oidc.ServiceClients[0].Enabled = false;
        Assert.Same(first, registry.Authenticate(Basic()));
        Assert.Null(new InquiryServicePrincipalRegistryV1(source, oidc).Authenticate(Basic()));
        Assert.Null(registry.Authenticate(Basic("wrong"))); Assert.Null(registry.Authenticate(new StringValues(new[] { Basic().ToString(), Basic().ToString() })));
        Assert.Null(registry.Authenticate("Bearer token")); Assert.DoesNotContain("owned-secret", first.ToString());
    }

    [Theory]
    [InlineData("version")][InlineData("unknown-client")][InlineData("duplicate-client")][InlineData("duplicate-subject")]
    [InlineData("subject")][InlineData("department")][InlineData("roles")][InlineData("duplicate-role")][InlineData("tenant")]
    [InlineData("secret")][InlineData("scope")]
    public void Invalid_registration_fails_before_it_can_produce_identity(string kind)
    {
        var (source, oidc) = Options(); var item = source.Registrations[0];
        switch (kind)
        {
            case "version": source.Version = ""; break;
            case "unknown-client": item.ClientId = "missing"; break;
            case "duplicate-client": source.Registrations.Add(item); break;
            case "duplicate-subject": source.Registrations.Add(new() { ClientId = "other", SubjectId = item.SubjectId }); break;
            case "subject": item.SubjectId = Guid.Empty; break;
            case "department": item.DepartmentId = Guid.Parse("11111111-1111-1111-8111-111111111111"); break;
            case "roles": item.RoleIds = []; break;
            case "duplicate-role": item.RoleIds = [1, 1]; break;
            case "tenant": oidc.Organizations[0].CrmEnabled = false; break;
            case "secret": oidc.ServiceClients[0].SecretSha256 = null!; break;
            case "scope": oidc.ServiceClients[0].AllowedScopes = ["other"]; break;
        }
        Assert.Equal("INQUIRY_SERVICE_REGISTRATION_INVALID", Assert.Throws<InvalidOperationException>(() =>
            new InquiryServicePrincipalRegistryV1(source, oidc)).Message);
    }
}
