using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceFieldPolicyTests
{
    [Fact]
    public void Policy_versions_changes_and_retirement_is_terminal()
    {
        var policy = SpaceFieldPolicy.Create(
            Guid.NewGuid(),
            " Customer portal ",
            SpaceExternalOrganizationType.Customer,
            false);

        Assert.Equal("Customer portal", policy.Name);
        Assert.Equal("CUSTOMER PORTAL", policy.NormalizedName);
        Assert.Equal(1, policy.PolicyVersion);

        policy.Update("Customer portal v2", true, SpaceFieldPolicyStatus.Retired);

        Assert.Equal(2, policy.PolicyVersion);
        Assert.True(policy.CanExport);
        Assert.Throws<SpaceExternalAccessStateException>(() => policy.Update(
            "reopen",
            false,
            SpaceFieldPolicyStatus.Active));
    }

    [Fact]
    public void Policy_and_field_reject_invalid_identity_and_values()
    {
        Assert.Throws<ArgumentException>(() => SpaceFieldPolicy.Create(
            Guid.NewGuid(),
            " ",
            SpaceExternalOrganizationType.Customer,
            false));
        Assert.Throws<SpaceExternalAccessStateException>(() =>
            SpaceFieldPolicy.Create(
                Guid.NewGuid(),
                "retired",
                SpaceExternalOrganizationType.Customer,
                false,
                SpaceFieldPolicyStatus.Retired));
        Assert.Throws<ArgumentException>(() => SpaceFieldPolicyField.Create(
            Guid.NewGuid(),
            Guid.Empty,
            SpaceFieldPolicyResourceType.Stock,
            "materialNumber",
            SpaceFieldMaskingRule.None));
    }

    [Fact]
    public void Field_catalog_is_explicit_and_case_insensitive()
    {
        var field = SpacePortalFieldCatalog.Find(
            SpaceResourceType.Stock,
            " MATERIALNUMBER ");

        Assert.NotNull(field);
        Assert.Equal("materialNumber", field.FieldName);
        Assert.Equal(SpacePortalFieldKind.Text, field.Kind);
        Assert.Null(SpacePortalFieldCatalog.Find(
            SpaceResourceType.Stock,
            "internalCost"));
    }
}
