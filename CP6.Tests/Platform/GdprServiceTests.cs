using System.Text;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Platform;

/// <summary>
/// 块③ GDPR 双粒度导出/擦除单测（直构服务 + InMemory + spy refresh）。覆盖：
/// 主体匿名化（[PiiField] 擦空 / UserName=anon / Enable=false / Password 变 / 行保留 Id 不变 / RevokeAll 被调）、
/// 防护 E-SEC-036（平台超管）/ E-SEC-037（最后超管）、整租户 anonymize（PII 擦 + 停租户 + 非 PII 不动）、
/// purge InMemory 抛 NotSupportedException、R6 纯函数（GetOwnerEntityTypes 含 OperLog 不含 Tenant / BuildDeleteOrder leaf-first）、
/// 导出剔密钥（JSON 无 Password）。
/// </summary>
public class GdprServiceTests
{
    private static readonly Guid TenantA = Guid.Parse("00000000-0000-0000-0000-0000000000A7");

    /// <summary>固定前缀的假哈希器（验"已重哈希"= 与原值不同，无需真 BCrypt 速度）。</summary>
    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string plain) => "HASH:" + plain;
        public bool Verify(string plain, string hash) => hash == "HASH:" + plain;
        public bool IsHashed(string value) => value.StartsWith("HASH:");
    }

    /// <summary>记录 RevokeAllForUserAsync 调用的 spy（其余方法 no-op）。</summary>
    private sealed class SpyRefresh : IRefreshTokenService
    {
        public readonly List<Guid> RevokedUsers = new();
        public Task<string> IssueAsync(Sys_User user, string? ip, string? ua) => Task.FromResult("rt");
        public Task<(string newToken, Sys_User user)> RotateAsync(string rawToken, string? ip, string? ua)
            => throw new NotImplementedException();
        public Task RevokeAsync(string rawToken) => Task.CompletedTask;
        public Task RevokeAllForUserAsync(Guid userId, bool saveChanges = true)
        {
            RevokedUsers.Add(userId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoopBlacklist : ITokenBlacklistService
    {
        public Task BlacklistAsync(string jti, TimeSpan ttl) => Task.CompletedTask;
        public Task<bool> IsBlacklistedAsync(string jti) => Task.FromResult(false);
    }

    private static (GdprService svc, CP6Context db, SpyRefresh refresh, TenantContext tenant) Make(Guid? currentTenant = null)
    {
        var tenant = new TenantContext { CurrentTenantId = currentTenant ?? TenantContext.DefaultTenant };
        var options = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new CP6Context(options, tenant);
        var refresh = new SpyRefresh();
        var audit = new SecurityAuditService(db);
        var svc = new GdprService(db, new FakeHasher(), refresh, new NoopBlacklist(), audit, tenant);
        return (svc, db, refresh, tenant);
    }

    private static Sys_User SeedUser(CP6Context db, Guid tenantId, bool isPlatformAdmin = false, bool enable = true,
        string? nick = "Nick", string? email = "u@x.com", string? ip = "1.2.3.4")
    {
        var u = new Sys_User
        {
            Id = Guid.NewGuid(),
            UserName = $"user_{Guid.NewGuid():N}",
            Password = "orig-password-hash",
            NickName = nick,
            Email = email,
            LastLoginIp = ip,
            TenantId = tenantId,
            IsPlatformAdmin = isPlatformAdmin,
            Enable = enable
        };
        db.Sys_Users.Add(u);
        db.SaveChanges();
        return u;
    }

    private static void SeedTenant(CP6Context db, Guid id, string code)
    {
        db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = code, TenantName = code, Enable = true });
        db.SaveChanges();
    }

    // ─────────────────────────── 主体擦除 ───────────────────────────

    [Fact]
    public async Task EraseSubject_AnonymizesPii_KeepsRowAndId_RevokesRefresh()
    {
        var (svc, db, refresh, _) = Make(currentTenant: TenantA);
        var u = SeedUser(db, TenantA);
        var originalId = u.Id;
        var originalPassword = u.Password;

        await svc.EraseSubjectAsync(u.Id);

        var after = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == originalId);
        Assert.Equal(originalId, after.Id);            // 行保留 + Id 不变（FK 完整）
        Assert.Null(after.NickName);                   // [PiiField Null]
        Assert.Null(after.Email);                      // [PiiField Null]
        Assert.Null(after.LastLoginIp);                // [PiiField Null]
        Assert.StartsWith("anon-", after.UserName);    // 显式匿名化
        Assert.False(after.Enable);                    // 停用
        Assert.NotEqual(originalPassword, after.Password);  // 密码重哈希
        Assert.Contains(originalId, refresh.RevokedUsers);  // RevokeAll 被调
    }

    [Fact]
    public async Task EraseSubject_AbsentUser_Throws_ESec032()
    {
        var (svc, _, _, _) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EraseSubjectAsync(Guid.NewGuid()));
        Assert.Equal("E-SEC-032", ex.Message);
    }

    [Fact]
    public async Task EraseSubject_PlatformTenantAdmin_Throws_ESec036()
    {
        var (svc, db, _, _) = Make();
        var u = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EraseSubjectAsync(u.Id));
        Assert.Equal("E-SEC-036", ex.Message);
    }

    [Fact]
    public async Task EraseSubject_LastEnabledPlatformAdmin_NonDefaultTenant_Throws_ESec037()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        var u = SeedUser(db, TenantA, isPlatformAdmin: true);   // 唯一启用平台超管，非默认租户
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.EraseSubjectAsync(u.Id));
        Assert.Equal("E-SEC-037", ex.Message);
    }

    [Fact]
    public async Task EraseSubject_NonLastPlatformAdmin_NonDefaultTenant_Succeeds()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        var target = SeedUser(db, TenantA, isPlatformAdmin: true);
        SeedUser(db, TenantA, isPlatformAdmin: true);   // 第二个启用超管 → 非最后一个
        await svc.EraseSubjectAsync(target.Id);
        var after = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == target.Id);
        Assert.False(after.Enable);
    }

    // ─────────────────────────── 整租户 anonymize ───────────────────────────

    [Fact]
    public async Task EraseTenant_Anonymize_ErasesUserPii_DisablesTenant()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        SeedTenant(db, TenantA, "TA");
        var u1 = SeedUser(db, TenantA, nick: "Alice", email: "a@x.com");
        var u2 = SeedUser(db, TenantA, nick: "Bob", email: "b@x.com");

        await svc.EraseTenantAsync(TenantA, "anonymize");

        var a1 = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == u1.Id);
        var a2 = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == u2.Id);
        Assert.Null(a1.NickName);
        Assert.Null(a1.Email);
        Assert.Null(a2.Email);
        Assert.False(a1.Enable);

        var t = await db.Sys_Tenants.IgnoreQueryFilters().FirstAsync(x => x.Id == TenantA);
        Assert.False(t.Enable);   // 停租户
    }

    [Fact]
    public async Task EraseTenant_PlatformTenant_Throws_ESec036()
    {
        var (svc, _, _, _) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EraseTenantAsync(TenantContext.DefaultTenant, "anonymize"));
        Assert.Equal("E-SEC-036", ex.Message);
    }

    [Fact]
    public async Task EraseTenant_InvalidMode_Throws_ESec038()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        SeedTenant(db, TenantA, "TA");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.EraseTenantAsync(TenantA, "shred"));
        Assert.Equal("E-SEC-038", ex.Message);
    }

    [Fact]
    public async Task EraseTenant_Purge_OnInMemory_Throws_NotSupported()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        SeedTenant(db, TenantA, "TA");
        SeedUser(db, TenantA);
        await Assert.ThrowsAsync<NotSupportedException>(() => svc.EraseTenantAsync(TenantA, "purge"));
    }

    // ─────────────────────────── R6 纯函数（拓扑） ───────────────────────────

    [Fact]
    public void GetOwnerEntityTypes_IncludesOperLog_ExcludesTenant()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var owners = TenantPurgeTopology.GetOwnerEntityTypes(db.Model);
        Assert.Contains(typeof(Sys_OperLog), owners);    // R6 统一判式：手加 TenantId 列亦纳入
        Assert.DoesNotContain(typeof(Sys_Tenant), owners);  // 共享表排除
        Assert.Contains(typeof(Sys_User), owners);
    }

    [Fact]
    public void BuildDeleteOrder_IsLeafFirst_EveryConfiguredFkChildBeforeParent()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var owners = new HashSet<Type>(TenantPurgeTopology.GetOwnerEntityTypes(db.Model));
        var (order, _) = TenantPurgeTopology.BuildDeleteOrder(db.Model);

        // 对每条 owner→owner 的真实配置 FK：子（child）须排在父（parent）之前（leaf-first 删除序）。
        var checkedAny = false;
        foreach (var et in db.Model.GetEntityTypes())
        {
            if (!owners.Contains(et.ClrType)) continue;
            foreach (var fk in et.GetForeignKeys())
            {
                var parent = fk.PrincipalEntityType.ClrType;
                if (parent == et.ClrType || !owners.Contains(parent)) continue;
                var childIdx = order.IndexOf(et.ClrType);
                var parentIdx = order.IndexOf(parent);
                Assert.True(childIdx >= 0 && parentIdx >= 0 && childIdx < parentIdx,
                    $"{et.ClrType.Name} (child) must be deleted before {parent.Name} (parent)");
                checkedAny = true;
            }
        }
        Assert.True(checkedAny, "expected at least one owner→owner FK pair to validate leaf-first ordering");

        // 每个 owner 类型都在删除序中恰出现（无遗漏）。
        foreach (var t in owners)
            Assert.Contains(t, order);
        Assert.Equal(owners.Count, order.Distinct().Count());
    }

    // ─────────────────────────── 导出剔密钥 ───────────────────────────

    [Fact]
    public async Task ExportTenant_JsonContainsUserButNoPassword()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        SeedTenant(db, TenantA, "TA");
        SeedUser(db, TenantA);

        var stream = await svc.ExportTenantAsync(TenantA);
        var json = ReadAll(stream);
        using var doc = JsonDocument.Parse(json);

        var data = doc.RootElement.GetProperty("data");
        Assert.True(data.TryGetProperty(nameof(Sys_User), out var users), "export must contain Sys_User rows");
        var first = users.EnumerateArray().First();
        Assert.False(first.TryGetProperty("Password", out _), "Password must be stripped from export");
        Assert.True(first.TryGetProperty("UserName", out _), "non-secret fields retained");
    }

    [Fact]
    public async Task ExportTenant_AbsentTenant_Throws_ESec032()
    {
        var (svc, _, _, _) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportTenantAsync(Guid.NewGuid()));
        Assert.Equal("E-SEC-032", ex.Message);
    }

    [Fact]
    public async Task ExportSubject_ContainsUser_StripsPassword()
    {
        var (svc, db, _, _) = Make(currentTenant: TenantA);
        var u = SeedUser(db, TenantA);

        var stream = await svc.ExportSubjectAsync(u.Id);
        var json = ReadAll(stream);
        using var doc = JsonDocument.Parse(json);

        var user = doc.RootElement.GetProperty("user");
        Assert.False(user.TryGetProperty("Password", out _), "Password must be stripped");
        Assert.Equal(u.UserName, user.GetProperty("UserName").GetString());
    }

    [Fact]
    public async Task ExportSubject_AbsentUser_Throws_ESec032()
    {
        var (svc, _, _, _) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ExportSubjectAsync(Guid.NewGuid()));
        Assert.Equal("E-SEC-032", ex.Message);
    }

    private static string ReadAll(Stream s)
    {
        s.Position = 0;
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return Encoding.UTF8.GetString(ms.ToArray());
    }
}
