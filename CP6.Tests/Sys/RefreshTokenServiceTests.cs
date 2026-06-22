using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 刷新令牌轮换 + 重用检测（S 类认证加固 T4）。错误经 <see cref="InvalidOperationException"/>
/// 携带 E-SEC 码（Core 层惯例，控制器转 BizException）。
/// </summary>
public class RefreshTokenServiceTests
{
    private static RefreshTokenService Make(CP6.Core.EFDbContext.CP6Context db)
        => new(db, Options.Create(new SecurityOptions { Token = new TokenOptions { RefreshTokenDays = 7 } }), new TenantContext());

    [Fact]
    public async Task Issue_then_rotate_invalidates_old_and_detects_reuse()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var user = new Sys_User { UserName = "u", Password = "h" };
        db.Sys_Users.Add(user);
        db.SaveChanges();
        var svc = Make(db);

        var raw1 = await svc.IssueAsync(user, "1.1.1.1", "UA");
        var (raw2, who) = await svc.RotateAsync(raw1, "1.1.1.1", "UA");
        Assert.Equal(user.Id, who.Id);
        Assert.NotEqual(raw1, raw2);

        // 轮换链真落库校验：清跟踪器强制重载（防 InMemory 导航修复假绿），旧行吊销且链向新行
        db.ChangeTracker.Clear();
        var rows = db.Sys_RefreshTokens.IgnoreQueryFilters().ToList();
        Assert.Equal(2, rows.Count);
        var oldRow = rows.Single(r => r.RevokedAt != null);
        var newRow = rows.Single(r => r.RevokedAt == null);
        Assert.Equal(oldRow.ReplacedByTokenHash, newRow.TokenHash);   // 旧行 ReplacedByTokenHash 指向新令牌哈希

        // 旧令牌已吊销 → 再用触发重用检测，整链吊销
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RotateAsync(raw1, "1.1.1.1", "UA"));
        // 重用检测后新令牌也被吊销 → 不可再轮换
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RotateAsync(raw2, "1.1.1.1", "UA"));
    }

    [Fact]
    public async Task Expired_is_rejected()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var user = new Sys_User { UserName = "u", Password = "h" };
        db.Sys_Users.Add(user);
        db.SaveChanges();
        var svc = Make(db);

        var raw = await svc.IssueAsync(user, null, null);
        var row = db.Sys_RefreshTokens.IgnoreQueryFilters().Single();
        row.ExpiresAt = DateTime.Now.AddMinutes(-1);
        db.SaveChanges();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RotateAsync(raw, null, null));
    }

    [Fact]
    public async Task Revoke_and_revoke_all_block_rotation()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var user = new Sys_User { UserName = "u", Password = "h" };
        db.Sys_Users.Add(user);
        db.SaveChanges();
        var svc = Make(db);

        // 单令牌吊销（如登出）→ 不再可轮换
        var rawA = await svc.IssueAsync(user, null, null);
        await svc.RevokeAsync(rawA);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RotateAsync(rawA, null, null));

        // 全量吊销（如改密）→ 现存有效令牌全部失效
        var rawB = await svc.IssueAsync(user, null, null);
        await svc.RevokeAllForUserAsync(user.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RotateAsync(rawB, null, null));
    }
}
