using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// UserController 密码落 BCrypt + 管理员重置（S 类认证加固 T7）：消除明文写入路径
/// （Add 建用户 / Update 改密）；管理员设密码强制改密 + 吊销该用户全部刷新令牌。
/// </summary>
public class UserControllerPasswordTests
{
    private static readonly IPasswordHasher H = new BCryptPasswordHasher();

    private static (UserController ctl, CP6Context db) Make()
    {
        var db = TestHelper.CreateInMemoryContext();
        var opt = Options.Create(new SecurityOptions());
        var refresh = new RefreshTokenService(db, opt, new TenantContext());
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var twoFa = new TwoFactorService(db, new TotpService(opt), new LogEmailSender(NullLogger<LogEmailSender>.Instance), cache, new SecurityAuditService(db), opt);
        var ctl = new UserController(db, H, refresh, twoFa)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (ctl, db);
    }

    [Fact]
    public async Task Add_hashes_password_and_forces_first_login_change()
    {
        var (ctl, db) = Make();

        var result = await ctl.Add(new Sys_User { UserName = "newuser", Password = "Init1al!" });

        Assert.IsType<OkObjectResult>(result);
        var u = db.Sys_Users.Single(x => x.UserName == "newuser");
        Assert.True(H.IsHashed(u.Password));            // 落 BCrypt，非明文
        Assert.True(H.Verify("Init1al!", u.Password));
        Assert.True(u.MustChangePassword);              // 管理员建用户 → 首登强制改密
        Assert.NotNull(u.PasswordChangedAt);
    }

    [Fact]
    public async Task Update_with_password_hashes_forces_change_and_revokes_refresh()
    {
        var (ctl, db) = Make();
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "bob", Password = H.Hash("Old1!pass"), Enable = true };
        db.Sys_Users.Add(user);
        db.Sys_RefreshTokens.Add(new Sys_RefreshToken { UserId = user.Id, TokenHash = "RT1", ExpiresAt = DateTime.Now.AddDays(7) });
        db.SaveChanges();

        await ctl.Update(new Sys_User { Id = user.Id, UserName = "bob", Enable = true, Password = "NewAdmin9#" });

        db.ChangeTracker.Clear();
        var u = db.Sys_Users.Single(x => x.Id == user.Id);
        Assert.True(H.Verify("NewAdmin9#", u.Password));   // 改密落 BCrypt
        Assert.True(u.MustChangePassword);                 // 管理员重置 → 强制改密
        var rt = db.Sys_RefreshTokens.IgnoreQueryFilters().Single(t => t.UserId == user.Id);
        Assert.NotNull(rt.RevokedAt);                      // 旧刷新令牌被吊销（强制下线）
    }

    [Fact]
    public async Task Update_without_password_keeps_hash_and_does_not_force_change()
    {
        var (ctl, db) = Make();
        var hash = H.Hash("Keep1!pass");
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "carol", Password = hash, Enable = true, MustChangePassword = false };
        db.Sys_Users.Add(user);
        db.SaveChanges();

        await ctl.Update(new Sys_User { Id = user.Id, UserName = "carol-renamed", Enable = true, Password = "" });

        db.ChangeTracker.Clear();
        var u = db.Sys_Users.Single(x => x.Id == user.Id);
        Assert.Equal(hash, u.Password);              // 未传密码 → 哈希原样不变
        Assert.False(u.MustChangePassword);          // 仅改资料不强制改密
        Assert.Equal("carol-renamed", u.UserName);
    }
}
