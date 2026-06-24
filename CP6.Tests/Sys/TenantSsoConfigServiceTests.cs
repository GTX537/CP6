using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>T2(SSO) ITenantSsoConfigService — DataProtection 加解密 + 密钥边界 + 跨租户读。</summary>
public class TenantSsoConfigServiceTests
{
    private static TenantSsoConfigService Make()
    {
        var db = TestHelper.CreateInMemoryContext();
        var dp = DataProtectionProvider.Create("CP6.Tests");
        return new TenantSsoConfigService(db, dp);
    }

    private static TenantSsoConfigService MakeOn(CP6Context db)
    {
        var dp = DataProtectionProvider.Create("CP6.Tests");
        return new TenantSsoConfigService(db, dp);
    }

    private static SsoConfigInput Input(string? secret = "s3cr3t", bool enabled = true) =>
        new(Authority: "https://login.example.com",
            ClientId: "cp6-sp",
            ClientSecret: secret,
            Scopes: "openid email profile",
            EmailClaim: "email",
            Enabled: enabled,
            Enforced: false,
            AutoProvision: true,
            DefaultRoleId: 99);

    [Fact]
    public async Task Upsert_then_get_decrypts_client_secret()
    {
        var svc = Make();
        var tid = Guid.NewGuid();
        await svc.UpsertAsync(tid, Input(secret: "s3cr3t"));

        var got = await svc.GetByTenantIdAsync(tid);
        Assert.NotNull(got);
        Assert.NotEqual("s3cr3t", got!.ClientSecretProtected);   // 不落明文
        Assert.False(string.IsNullOrEmpty(got.ClientSecretProtected));
        Assert.Equal("s3cr3t", svc.DecryptClientSecret(got));     // 还原
        Assert.Equal("https://login.example.com", got.Authority);
        Assert.Equal(tid, got.TenantId);
    }

    [Fact]
    public async Task Upsert_empty_secret_keeps_existing()
    {
        // 共用同一 DbContext，验"二次 Upsert 不清空旧密文"
        using var db = TestHelper.CreateInMemoryContext();
        var svc = MakeOn(db);
        var tid = Guid.NewGuid();
        await svc.UpsertAsync(tid, Input(secret: "originalSecret"));
        var first = await svc.GetByTenantIdAsync(tid);
        var firstProtected = first!.ClientSecretProtected!;

        // 二次 Upsert ClientSecret=null → 不动密文，其它字段可改
        await svc.UpsertAsync(tid, Input(secret: null, enabled: false));
        var second = await svc.GetByTenantIdAsync(tid);

        Assert.Equal(firstProtected, second!.ClientSecretProtected);
        Assert.Equal("originalSecret", svc.DecryptClientSecret(second));   // 仍可解原值
        Assert.False(second.Enabled);   // 其它字段被覆盖
    }

    [Fact]
    public async Task Upsert_empty_string_secret_also_keeps_existing()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = MakeOn(db);
        var tid = Guid.NewGuid();
        await svc.UpsertAsync(tid, Input(secret: "kept"));
        await svc.UpsertAsync(tid, Input(secret: ""));   // 空串也视同不改

        var got = await svc.GetByTenantIdAsync(tid);
        Assert.Equal("kept", svc.DecryptClientSecret(got!));
    }

    [Fact]
    public async Task GetByTenantId_uses_ignore_filters()
    {
        // 双租户共库；任一租户上下文均能按 Id 取到他租配置（登录期无上下文，必须 IgnoreQueryFilters）
        using var db = TestHelper.CreateInMemoryContext();
        var svc = MakeOn(db);
        var tA = Guid.NewGuid();
        var tB = Guid.NewGuid();
        await svc.UpsertAsync(tA, Input(secret: "A"));
        await svc.UpsertAsync(tB, Input(secret: "B"));

        Assert.NotNull(await svc.GetByTenantIdAsync(tA));
        Assert.NotNull(await svc.GetByTenantIdAsync(tB));
    }

    [Fact]
    public async Task GetByTenantCode_resolves_via_Sys_Tenant()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var svc = MakeOn(db);
        var tid = Guid.NewGuid();
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tid, TenantCode = "ACME", TenantName = "Acme", Enable = true });
        await db.SaveChangesAsync();
        await svc.UpsertAsync(tid, Input(secret: "code-path"));

        var got = await svc.GetByTenantCodeAsync("ACME");
        Assert.NotNull(got);
        Assert.Equal(tid, got!.TenantId);
        Assert.Equal("code-path", svc.DecryptClientSecret(got));

        Assert.Null(await svc.GetByTenantCodeAsync("NOPE"));   // 不存在租户码 → null
    }

    [Fact]
    public void DecryptClientSecret_throws_when_protected_is_empty()
    {
        var svc = Make();
        var cfg = new Sys_TenantSsoConfig { ClientSecretProtected = null };
        var ex = Assert.Throws<InvalidOperationException>(() => svc.DecryptClientSecret(cfg));
        Assert.Equal("E-SEC-028", ex.Message);
    }
}
