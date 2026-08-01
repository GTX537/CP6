using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceExternalGrantTests
{
    [Fact]
    public void Grant_updates_increment_version_and_revocation_is_terminal()
    {
        var now = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var grant = SpaceExternalGrant.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            false,
            now,
            now.AddDays(10),
            SpaceExternalGrantStatus.Active);

        grant.Update(
            Guid.NewGuid(),
            null,
            true,
            now,
            null,
            SpaceExternalGrantStatus.Suspended);
        Assert.Equal(2, grant.GrantVersion);
        Assert.True(grant.CanExport);

        grant.Update(
            grant.SiteId,
            null,
            false,
            now,
            null,
            SpaceExternalGrantStatus.Revoked);
        Assert.Equal(3, grant.GrantVersion);
        Assert.Throws<SpaceExternalAccessStateException>(() => grant.Update(
            grant.SiteId,
            null,
            false,
            now,
            null,
            SpaceExternalGrantStatus.Active));
    }

    [Fact]
    public void Grant_rejects_invalid_validity_and_revoked_creation()
    {
        var now = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Throws<ArgumentException>(() => SpaceExternalGrant.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            false,
            now,
            now,
            SpaceExternalGrantStatus.Active));
        Assert.Throws<SpaceExternalAccessStateException>(() =>
            SpaceExternalGrant.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                false,
                now,
                null,
                SpaceExternalGrantStatus.Revoked));
    }

    [Fact]
    public void Scope_values_are_trimmed_and_normalized()
    {
        var tenantId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var owner = SpaceExternalGrantOwner.Create(
            tenantId,
            grantId,
            " owner-a ");
        var businessObject = SpaceExternalGrantObject.Create(
            tenantId,
            grantId,
            " task ",
            " pick-1 ");

        Assert.Equal("owner-a", owner.OwnerId);
        Assert.Equal("OWNER-A", owner.NormalizedOwnerId);
        Assert.Equal("task", businessObject.BusinessObjectType);
        Assert.Equal("TASK", businessObject.NormalizedBusinessObjectType);
        Assert.Equal("pick-1", businessObject.BusinessObjectId);
        Assert.Equal("PICK-1", businessObject.NormalizedBusinessObjectId);
    }

    [Fact]
    public void Retired_scope_is_soft_deleted()
    {
        var scope = SpaceExternalGrantFloor.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());

        scope.Retire();

        Assert.True(scope.IsDeleted);
    }
}
