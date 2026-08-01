using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceExternalAccessTests
{
    private static readonly DateTime Now =
        new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Organization_normalizes_identity_and_starts_versioned()
    {
        var organization = SpaceExternalOrganization.Create(
            Guid.NewGuid(),
            SpaceExternalOrganizationType.Customer,
            "  customer-a  ",
            " Customer A ");

        Assert.Equal("customer-a", organization.Code);
        Assert.Equal("CUSTOMER-A", organization.NormalizedCode);
        Assert.Equal("Customer A", organization.Name);
        Assert.Equal(1, organization.SecurityStamp);
        Assert.Equal(SpaceExternalOrganizationStatus.Active, organization.Status);
    }

    [Fact]
    public void Closed_organization_is_terminal()
    {
        var organization = SpaceExternalOrganization.Create(
            Guid.NewGuid(),
            SpaceExternalOrganizationType.Supplier,
            "supplier-a",
            "Supplier A");
        organization.Update(
            "supplier-a",
            "Supplier A",
            null,
            null,
            SpaceExternalOrganizationStatus.Closed);

        Assert.Throws<SpaceExternalAccessStateException>(() =>
            organization.Update(
                "supplier-a",
                "Supplier A",
                null,
                null,
                SpaceExternalOrganizationStatus.Active));
        Assert.Throws<SpaceExternalAccessStateException>(
            organization.TouchMembershipSecurityStamp);
    }

    [Fact]
    public void Membership_activation_is_auditable_and_revocation_is_terminal()
    {
        var membership = SpaceExternalMembership.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            SpaceExternalMembershipRole.Viewer,
            Now,
            Now.AddDays(7),
            SpaceExternalMembershipStatus.Invited,
            Guid.NewGuid(),
            Now);

        membership.Update(
            SpaceExternalMembershipRole.OperationsViewer,
            Now,
            Now.AddDays(14),
            SpaceExternalMembershipStatus.Active,
            Now.AddMinutes(1));
        Assert.Equal(Now.AddMinutes(1), membership.AcceptedAtUtc);
        Assert.Equal(2, membership.SecurityStamp);

        membership.Update(
            membership.Role,
            membership.ValidFromUtc,
            membership.ValidToUtc,
            SpaceExternalMembershipStatus.Revoked,
            Now.AddMinutes(2));
        Assert.Throws<SpaceExternalAccessStateException>(() =>
            membership.Update(
                membership.Role,
                membership.ValidFromUtc,
                membership.ValidToUtc,
                SpaceExternalMembershipStatus.Active,
                Now.AddMinutes(3)));
    }

    [Fact]
    public void Membership_rejects_non_utc_or_inverted_validity()
    {
        var tenantId = Guid.NewGuid();
        var organizationId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            SpaceExternalMembership.Create(
                tenantId,
                organizationId,
                userId,
                SpaceExternalMembershipRole.Viewer,
                DateTime.SpecifyKind(Now, DateTimeKind.Unspecified),
                null,
                SpaceExternalMembershipStatus.Invited,
                null,
                Now));
        Assert.Throws<ArgumentException>(() =>
            SpaceExternalMembership.Create(
                tenantId,
                organizationId,
                userId,
                SpaceExternalMembershipRole.Viewer,
                Now,
                Now,
                SpaceExternalMembershipStatus.Invited,
                null,
                Now));
    }
}
