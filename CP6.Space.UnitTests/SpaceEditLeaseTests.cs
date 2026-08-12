using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceEditLeaseTests
{
    private static readonly DateTime Now =
        new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Lease_expires_after_the_configured_slot()
    {
        var lease = Create();

        Assert.False(lease.IsExpired(Now.AddSeconds(89)));
        Assert.True(lease.IsExpired(Now.AddSeconds(90)));
    }

    [Fact]
    public void Renew_requires_the_current_owner_and_active_lease()
    {
        var lease = Create();

        Assert.Throws<InvalidOperationException>(() => lease.Renew(
            Guid.NewGuid(),
            lease.OwnerUserId,
            Now.AddSeconds(30),
            TimeSpan.FromSeconds(90)));
        Assert.Throws<InvalidOperationException>(() => lease.Renew(
            lease.LeaseId,
            Guid.NewGuid(),
            Now.AddSeconds(30),
            TimeSpan.FromSeconds(90)));

        lease.Renew(
            lease.LeaseId,
            lease.OwnerUserId,
            Now.AddSeconds(30),
            TimeSpan.FromSeconds(90));
        Assert.Equal(Now.AddSeconds(120), lease.ExpiresAtUtc);
    }

    [Fact]
    public void Reassign_rotates_the_lease_identity()
    {
        var lease = Create();
        var oldLeaseId = lease.LeaseId;
        var newOwner = Guid.NewGuid();
        var newClient = Guid.NewGuid();

        lease.Reassign(
            newOwner,
            newClient,
            Now.AddSeconds(10),
            TimeSpan.FromSeconds(90));

        Assert.NotEqual(oldLeaseId, lease.LeaseId);
        Assert.True(lease.IsOwnedBy(newOwner, newClient));
    }

    [Fact]
    public void Takeover_audit_requires_a_bounded_reason()
    {
        Assert.Throws<ArgumentException>(() =>
            SpaceEditLeaseTakeoverAudit.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "   ",
                Now));
    }

    [Fact]
    public void Command_batch_records_the_edit_lease_identity()
    {
        var leaseId = Guid.NewGuid();
        var batch = SpaceElementCommandBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            leaseId,
            4,
            new string('a', 64),
            Guid.NewGuid(),
            Now);

        Assert.Equal(leaseId, batch.LeaseId);
    }

    private static SpaceEditLease Create() => SpaceEditLease.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Now,
        TimeSpan.FromSeconds(90));
}
