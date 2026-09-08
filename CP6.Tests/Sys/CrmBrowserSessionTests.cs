using System.Security.Cryptography;
using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace CP6.Tests.Sys;

public sealed class CrmBrowserSessionTests
{
    [Theory]
    [InlineData("enroll")]
    [InlineData("reset")]
    [InlineData("enroll-reset")]
    [InlineData("password")]
    public async Task Original_browser_authentication_cannot_refresh_after_security_change(string change)
    {
        using var f = new Fixture();
        if (change == "reset")
        {
            f.User.TwoFactorEnabled = true;
            f.User.TwoFactorSecret = "original-secret";
        }
        await f.Db.SaveChangesAsync();
        var raw = await f.Refresh.IssueAsync(f.User, null, null);
        await f.ChangeSecurityAsync(change);

        var error = await Record.ExceptionAsync(() => f.Refresh.RotateAsync(raw, null, null));

        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("E-SEC-007", error.Message);
    }

    [Fact]
    public async Task Normal_browser_rotation_does_not_limit_family_length()
    {
        using var f = new Fixture();
        var root = await f.Refresh.IssueAsync(f.User, null, null);
        var current = root;
        for (var i = 0; i < 70; i++) current = (await f.Refresh.RotateAsync(current, null, null)).newToken;
        var directory = new CrmOidcDirectory(f.Db, f.Tenant, Mock.Of<ITokenBlacklistService>(),
            Mock.Of<IPasswordPolicyService>(), new CrmOidcOptions { Enabled = true });
        var grant = new CrmOidcGrant { SourceRefreshHash = Hash(root), SubjectId = f.User.Id, OrganizationId = f.User.TenantId };

        Assert.True(await directory.RefreshFamilyMatchesAsync(grant));
        Assert.True(await directory.RefreshFamilyMatchesAsync(grant, current));
    }

    [Theory]
    [InlineData("Web", false)]
    [InlineData("Windows", true)]
    [InlineData("Android", true)]
    public async Task Disabled_bridge_and_native_refresh_keep_legacy_behavior(string kind, bool bridgeEnabled)
    {
        using var f = new Fixture();
        f.Config["CrmOidc:Enabled"] = bridgeEnabled.ToString();
        var client = new RefreshTokenClientContext(kind, kind == "Web" ? null : "device", "1");
        var raw = await f.Refresh.IssueAsync(f.User, null, null, client);
        await f.ChangeSecurityAsync("enroll");

        var (next, _) = await f.Refresh.RotateAsync(raw, null, null, client);

        Assert.Null((await f.Db.Sys_RefreshTokens.IgnoreQueryFilters().SingleAsync(r => r.TokenHash == Hash(next))).BrowserSessionId);
        Assert.Empty(f.Db.Sys_BrowserSessions.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Enabling_bridge_does_not_upgrade_an_existing_legacy_refresh_family()
    {
        using var f = new Fixture();
        f.Config["CrmOidc:Enabled"] = "false";
        var raw = await f.Refresh.IssueAsync(f.User, null, null);
        f.Config["CrmOidc:Enabled"] = "true";
        var (next, _) = await f.Refresh.RotateAsync(raw, null, null);
        var directory = new CrmOidcDirectory(f.Db, f.Tenant, Mock.Of<ITokenBlacklistService>(),
            Mock.Of<IPasswordPolicyService>(), new CrmOidcOptions { Enabled = true });

        Assert.False(await directory.HasCurrentBrowserAuthenticationAsync(Hash(next), f.User));
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class Fixture : IDisposable
    {
        public readonly TenantContext Tenant = new() { CurrentTenantId = Guid.NewGuid() };
        public readonly CP6Context Db;
        public readonly Sys_User User;
        public readonly RefreshTokenService Refresh;
        public readonly IConfiguration Config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["CrmOidc:Enabled"] = "true" }).Build();
        public Fixture()
        {
            Db = new CP6Context(new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, Tenant);
            User = new Sys_User { Id = Guid.NewGuid(), TenantId = Tenant.CurrentTenantId, UserName = "original-login", Password = "original-hash" };
            Db.Sys_Users.Add(User);
            Db.SaveChanges();
            using var services = new ServiceCollection().AddSingleton(Config).BuildServiceProvider();
            Refresh = ActivatorUtilities.CreateInstance<RefreshTokenService>(services, Db, Options.Create(new SecurityOptions()), Tenant);
        }
        public async Task ChangeSecurityAsync(string change)
        {
            var totp = new Mock<ITotpService>();
            totp.Setup(t => t.Verify(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
            var twoFactor = new TwoFactorService(Db, totp.Object, Mock.Of<IEmailSender>(), Mock.Of<IDistributedCache>(),
                Mock.Of<ISecurityAuditService>(), Options.Create(new SecurityOptions()));
            if (change is "enroll" or "enroll-reset")
            {
                User.TwoFactorSecret = "new-secret";
                Assert.True(await twoFactor.ConfirmEnrollmentAsync(User, "verified"));
            }
            if (change is "reset" or "enroll-reset") await twoFactor.ResetAsync(User, "test-security-transition");
            if (change == "password")
            {
                User.Password = "new-hash";
                User.PasswordChangedAt = DateTime.Now;
            }
            await Db.SaveChangesAsync();
        }
        public void Dispose() => Db.Dispose();
    }
}
