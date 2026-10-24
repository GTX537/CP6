using System.Text;
using System.Text.Json.Nodes;
using CP6.Core.Services.CrmIdentity;
using CP6.Platform.Messaging;

namespace CP6.Tests;

public class CrmIdentityContractTests
{
    private const string Issuer = "https://identity.cp6.test";
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "contracts/events/platform");
    private static readonly Cp6ContractBundle Bundle = Cp6ContractBundle.Load(Root);
    private readonly IdentityEventValidator validator = new(Bundle, Issuer);

    public static IEnumerable<object[]> Examples() => Bundle.Entries.SelectMany(entry => entry.Examples
        .Select(example => new object[] { example.Path, example.Valid }));

    [Theory]
    [MemberData(nameof(Examples))]
    public void Contract_matrix_is_enforced(string path, bool expected)
        => Assert.Equal(expected, validator.IsValid(File.ReadAllBytes(Bundle.GetAssetPath(path))));

    [Theory]
    [InlineData("tenant.changed", "source", "urn:cp6:crm")]
    [InlineData("token.revoked", "data.issuer", "https://untrusted.cp6.test")]
    [InlineData("user.changed", "data.tenantId", "22222222-2222-4222-8222-222222222222")]
    [InlineData("user.changed", "aggregateid", "permission:user:33333333-3333-4333-8333-333333333333")]
    [InlineData("user.changed", "subject", "tenants/22222222-2222-4222-8222-222222222222/user")]
    [InlineData("department.changed", "data.path", "/22222222-2222-4222-8222-222222222222/")]
    public void Rejects_inconsistent_identity(string name, string field, string value)
    {
        var node = Example(name);
        if (field.StartsWith("data.")) node["data"]![field[5..]] = value;
        else node[field] = value;
        Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void Rejects_data_version_that_differs_from_envelope()
    {
        var node = Example("permission.changed");
        node["data"]!["version"] = 2;
        Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Theory]
    [InlineData("\"version\":1,\"version\":1")]
    [InlineData("\"extra\":{\"value\":1,\"value\":2},\"version\":1")]
    [InlineData("\"extra\":{\"Email\":\"private@example.test\"},\"version\":1")]
    [InlineData("\"extra\":{\"access_token\":\"secret\"},\"version\":1")]
    public void Rejects_duplicate_keys_and_nested_sensitive_extensions(string replacement)
    {
        var json = Example("user.changed").ToJsonString().Replace("\"version\":1", replacement);
        Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public void Empty_grants_is_a_valid_atomic_authorization_removal()
    {
        var node = Example("permission.changed");
        node["data"]!["grants"] = new JsonArray();
        Assert.True(validator.IsValid(Encoding.UTF8.GetBytes(node.ToJsonString())));
    }

    [Fact]
    public void Valid_factory_envelope_passes_the_same_validator()
    {
        var tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var data = new TenantIdentityData(tenant, false, 7, null);
        var envelope = IdentityEventContracts.Create(tenant, "tenant:" + tenant, 7,
            IdentityEventContracts.TenantChanged, data, "us", "correlation-1", "command-1");
        Assert.True(validator.Validate(envelope).IsValid);
        Assert.False(validator.Validate(envelope with { PartitionKey = "wrong" }).IsValid);
        Assert.False(validator.Validate(envelope with { AggregateVersion = 8 }).IsValid);
    }

    [Theory]
    [InlineData("role:-2147483648", true)]
    [InlineData("role:-1", true)]
    [InlineData("role:0", true)]
    [InlineData("role:1", true)]
    [InlineData("role:2147483647", true)]
    [InlineData("role:-2147483649", false)]
    [InlineData("role:2147483648", false)]
    [InlineData("role:-0", false)]
    [InlineData("role:+1", false)]
    [InlineData("role:01", false)]
    [InlineData("role:-01", false)]
    [InlineData("role: 0", false)]
    public void Full_integer_contract_preserves_exact_identity_and_rejects_aliases(string subject, bool valid)
    {
        foreach (var type in new[] { "user.changed", "permission.changed", "token.revoked" })
        {
            var node = Example(type, 2);
            if (type == "user.changed") node["data"]!["roles"] = new JsonArray(subject);
            else node["data"]!["subjectId"] = subject;
            if (type == "permission.changed")
            {
                node["aggregateid"] = "permission:" + subject;
                node["subject"] = $"tenants/{node["tenantid"]!.GetValue<string>()}/permission:{subject}";
            }
            Assert.Equal(valid, validator.IsValid(Encoding.UTF8.GetBytes(node.ToJsonString())));
        }
    }

    [Fact]
    public void Role_subject_is_canonical_even_when_current_culture_uses_a_different_negative_sign()
    {
        var original=System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var custom=(System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
            custom.NumberFormat.NegativeSign="~";System.Globalization.CultureInfo.CurrentCulture=custom;
            Assert.Equal("role:-2147483648",IdentityEventContracts.RoleSubject(int.MinValue));
            Assert.True(IdentityEventContracts.TryRoleSubject("role:-2147483648",out var role));Assert.Equal(int.MinValue,role);
            Assert.False(IdentityEventContracts.TryRoleSubject("role:~1",out _));
        }
        finally{System.Globalization.CultureInfo.CurrentCulture=original;}
    }

    [Fact]
    public void V1_stays_positive_and_old_consumer_rejects_v2_instead_of_losing_roles()
    {
        using var legacy = new LegacyIdentityBundle();
        var oldConsumer = new IdentityEventValidator(legacy.Bundle, Issuer);
        foreach (var type in new[] { "user.changed", "permission.changed", "token.revoked" })
        {
            Assert.True(oldConsumer.IsValid(Encoding.UTF8.GetBytes(Example(type).ToJsonString())));
            var v2 = Encoding.UTF8.GetBytes(Example(type, 2).ToJsonString());
            Assert.True(validator.IsValid(v2));
            Assert.False(oldConsumer.IsValid(v2));
            var unknown = Example(type, 2); unknown["type"] = "com.gtx537.platform." + type + ".v3";
            Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(unknown.ToJsonString())));
            var mismatch = Example(type, 2); mismatch["schemaversion"] = "1.0.0";
            Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(mismatch.ToJsonString())));
        }
        var user = Example("user.changed"); user["data"]!["roles"] = new JsonArray("role:0");
        Assert.False(validator.IsValid(Encoding.UTF8.GetBytes(user.ToJsonString())));
    }

    [Fact]
    public void Service_membership_preserves_all_integer_roles_within_original_transport_limit()
    {
        var tenant=Guid.NewGuid();var principal=Guid.NewGuid();var data=new ServicePrincipalIdentityData(tenant,principal,"inquiry-h01",Guid.NewGuid(),true,1,
            Enumerable.Range(-512,1024).Select(IdentityEventContracts.RoleSubject).ToArray(),Guid.NewGuid(),"v1",1);
        var envelope=IdentityEventContracts.Create(tenant,"service-principal:"+principal.ToString("D"),1,IdentityEventContracts.ServicePrincipalChanged,data,"us","c","c");
        Assert.True(validator.Validate(envelope).IsValid);
    }

    private static JsonObject Example(string name, int major = 1) => JsonNode.Parse(File.ReadAllBytes(Bundle.GetAssetPath(
        Bundle.Entries.Single(x => x.EventType == "com.gtx537.platform." + name + ".v" + major)
            .Examples.Single(x => x.Name == "valid").Path)))!.AsObject();
}
