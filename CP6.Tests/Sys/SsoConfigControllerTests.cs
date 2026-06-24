using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// #3 SSO（T7）SsoConfigController：GET 投影不返密文 / PUT upsert（空密钥不改）。
/// 直接 new 控制器（绕 [RequirePermission] 过滤器）+ InMemory + DataProtection 测试 provider。
/// </summary>
public class SsoConfigControllerTests
{
    private static (SsoConfigController ctl, CP6Context db, Guid tid) Make()
    {
        var db = TestHelper.CreateInMemoryContext();
        var svc = new TenantSsoConfigService(db, DataProtectionProvider.Create("CP6.Tests"));
        var tid = Guid.NewGuid();
        var ctl = new SsoConfigController(svc, new TenantContext { CurrentTenantId = tid }, new SecurityAuditService(db));
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (ctl, db, tid);
    }

    private static SsoConfigInput Input(string? secret = "s3cr3t", bool enabled = true, string authority = "https://idp.example") =>
        new(Authority: authority, ClientId: "cp6-sp", ClientSecret: secret, Scopes: "openid email profile",
            EmailClaim: "email", Enabled: enabled, Enforced: false, AutoProvision: true, DefaultRoleId: 99);

    private static T? Prop<T>(object obj, string name) => (T?)obj.GetType().GetProperty(name)!.GetValue(obj);

    [Fact]
    public async Task Get_returns_projection_without_secret()
    {
        var (ctl, _, _) = Make();
        // 经控制器 PUT 落库再 GET，确保走同一服务/库
        await ctl.Put(Input(secret: "s3cr3t"));

        var ok = Assert.IsType<OkObjectResult>(await ctl.Get());
        var v = ok.Value!;
        Assert.True(Prop<bool>(v, "hasClientSecret"));
        Assert.Equal("https://idp.example", Prop<string>(v, "authority"));
        Assert.True(Prop<bool>(v, "enabled"));
        // 投影绝不含密文/明文字段
        Assert.Null(v.GetType().GetProperty("clientSecretProtected"));
        Assert.Null(v.GetType().GetProperty("clientSecret"));
    }

    [Fact]
    public async Task Put_upserts_then_empty_secret_keeps_existing()
    {
        var (ctl, db, tid) = Make();
        await ctl.Put(Input(secret: "originalSecret", enabled: true));
        var firstProtected = db.Sys_TenantSsoConfigs.IgnoreQueryFilters().Single(x => x.TenantId == tid).ClientSecretProtected;
        Assert.False(string.IsNullOrEmpty(firstProtected));

        // 二次 PUT 空密钥 + 改 Enabled → 密文不变、其它字段更新
        await ctl.Put(Input(secret: "", enabled: false));
        var row = db.Sys_TenantSsoConfigs.IgnoreQueryFilters().Single(x => x.TenantId == tid);
        Assert.Equal(firstProtected, row.ClientSecretProtected);
        Assert.False(row.Enabled);

        // 审计：两次 PUT 各记一条 SsoConfigChanged
        Assert.Equal(2, db.Sys_SecurityLogs.IgnoreQueryFilters().Count(l => l.EventType == (int)SecurityEventType.SsoConfigChanged));
    }
}
