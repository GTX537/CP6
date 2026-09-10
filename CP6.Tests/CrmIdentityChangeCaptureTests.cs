using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using CP6.Platform.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests;

public class CrmIdentityChangeCaptureTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static CrmIdentityOptions Options => new() { Enabled = true, Tenants = { [Tenant] = "us" } };
    private static CP6Context Context() => new(new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public void Unchanged_user_or_display_only_edit_does_not_capture_identity_work()
    {
        using var db = Context();
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = Tenant, UserName = "fixture" };
        db.Attach(user);
        Assert.False(IdentityChangeCapture.Capture(db, Options).HasChanges);
        user.Email = "private@example.test";
        user.LastLoginTime = DateTime.Now;
        Assert.False(IdentityChangeCapture.Capture(db, Options).HasChanges);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("password")]
    [InlineData("epoch")]
    [InlineData("two-factor")]
    [InlineData("lock")]
    [InlineData("delete")]
    public void Identity_invalidating_changes_capture_the_actual_user(string action)
    {
        using var db = Context();
        var user = new Sys_User { Id = Guid.NewGuid(), TenantId = Tenant, UserName = "fixture" };
        db.Attach(user);
        switch (action)
        {
            case "disable": user.Enable = false; break;
            case "password": user.Password = "changed-fixture-hash"; break;
            case "epoch": user.AuthenticationEpoch = Guid.NewGuid(); break;
            case "two-factor": user.TwoFactorEnabled = true; break;
            case "lock": user.LockedUntil = DateTime.Now.AddMinutes(5); break;
            case "delete": db.Remove(user); break;
        }
        var capture = IdentityChangeCapture.Capture(db, Options);
        Assert.Contains((Tenant, $"user:{user.Id:D}"), capture.Targets);
        Assert.Contains((Tenant, user.Id), capture.InvalidatedUsers);
    }

    [Fact]
    public void Role_and_user_membership_changes_keep_distinct_aggregate_identities()
    {
        using var db = Context();
        var user = Guid.NewGuid();
        db.Add(new Sys_Role { TenantId = Tenant, RoleId = 7 });
        db.Add(new Sys_UserRole { TenantId = Tenant, UserId = user, RoleId = 7 });
        var capture = IdentityChangeCapture.Capture(db, Options);
        Assert.Equal(2, capture.Targets.Count);
        Assert.Contains((Tenant, "permission:role:7"), capture.Targets);
        Assert.Contains((Tenant, $"user:{user:D}"), capture.Targets);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ordinary_refresh_rotation_is_not_a_terminal_family_revocation(bool replaced)
    {
        using var db = Context();
        var row = new Sys_RefreshToken { Id = Guid.NewGuid(), TenantId = Tenant, UserId = Guid.NewGuid(), BrowserSessionId = Guid.NewGuid() };
        db.Attach(row);
        row.RevokedAt = DateTime.Now;
        row.ReplacedByTokenHash = replaced ? "new-fixture-hash" : null;
        var capture = IdentityChangeCapture.Capture(db, Options);
        Assert.Equal(replaced ? 0 : 1, capture.RevokedFamilies.Count);
    }

    [Fact]
    public void Unconfigured_tenant_does_not_publish()
    {
        using var db = Context();
        db.Add(new Sys_User { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() });
        Assert.False(IdentityChangeCapture.Capture(db, Options).HasChanges);
    }

    [Fact]
    public void Purge_keeps_protocol_tombstones_but_removes_service_records_and_business_rows()
    {
        using var db = Context();
        var owned = TenantPurgeTopology.GetOwnerEntityTypes(db.Model);
        Assert.Contains(typeof(Sys_User), owned);
        Assert.Contains(typeof(CrmServiceTokenRecord), owned);
        Assert.DoesNotContain(typeof(CrmIdentitySnapshot), owned);
        Assert.DoesNotContain(typeof(Cp6OutboxMessage), owned);
    }
}
