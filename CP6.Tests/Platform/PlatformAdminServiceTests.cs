using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

/// <summary>
/// 块② 平台超管授撤服务单测（直构服务 + InMemory）。覆盖：授予列出、目标不存在 E-SEC-032、
/// 防自锁死撤最后一个超管 E-SEC-037、撤非最后超管成功、授撤审计落平台租户（R5）。
/// </summary>
public class PlatformAdminServiceTests
{
    private static readonly Guid SomeOtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000B7");

    private static (PlatformAdminService svc, CP6Context db) Make(Guid? currentTenant = null)
    {
        var tenant = new TenantContext { CurrentTenantId = currentTenant ?? TenantContext.DefaultTenant };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CP6Context(options, tenant);   // 同一 TenantContext → 服务与 db 共享上下文
        var audit = new SecurityAuditService(db);
        var svc = new PlatformAdminService(db, audit, tenant);
        return (svc, db);
    }

    private static Guid SeedUser(CP6Context db, Guid tenantId, bool isPlatformAdmin = false, bool enable = true)
    {
        var id = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User
        {
            Id = id,
            UserName = $"u_{id:N}",
            NickName = "nick",
            Password = "x",
            TenantId = tenantId,
            IsPlatformAdmin = isPlatformAdmin,
            Enable = enable
        });
        db.SaveChanges();
        return id;
    }

    // ── 授予普通用户 → ListAsync 含其行，DB IsPlatformAdmin=true。
    [Fact]
    public async Task Grant_NormalUser_AppearsInList_AndFlagSetInDb()
    {
        var (svc, db) = Make(currentTenant: SomeOtherTenant);
        var uid = SeedUser(db, SomeOtherTenant);

        await svc.GrantAsync(uid);

        var rows = await svc.ListAsync();
        Assert.Contains(rows, r => r.Id == uid);

        var u = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == uid);
        Assert.True(u.IsPlatformAdmin);
    }

    // ── 授予不存在用户 → E-SEC-032。
    [Fact]
    public async Task Grant_AbsentUser_Throws_ESec032()
    {
        var (svc, _) = Make();
        Assert.Equal("E-SEC-032",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.GrantAsync(Guid.NewGuid()))).Message);
    }

    // ── 撤销最后一个启用超管 → E-SEC-037，且 DB 仍保留其超管标志（未被撤）。
    [Fact]
    public async Task Revoke_LastEnabledPlatformAdmin_Throws_ESec037_AndKeepsFlag()
    {
        var (svc, db) = Make();
        var soleAdmin = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        Assert.Equal("E-SEC-037",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RevokeAsync(soleAdmin))).Message);

        var u = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == soleAdmin);
        Assert.True(u.IsPlatformAdmin);   // 未被撤
    }

    // ── 2+ 启用超管时撤其一 → 成功，目标 IsPlatformAdmin=false。
    [Fact]
    public async Task Revoke_WhenMultipleEnabledAdmins_Succeeds()
    {
        var (svc, db) = Make();
        var a = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);
        SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);   // 第二个启用超管

        await svc.RevokeAsync(a);

        var u = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == a);
        Assert.False(u.IsPlatformAdmin);
    }

    // ── 撤已停用超管：即便是唯一启用超管之外的已停用超管，也允许（不计入把关）。
    [Fact]
    public async Task Revoke_DisabledAdmin_Succeeds_EvenIfOneEnabledRemains()
    {
        var (svc, db) = Make();
        SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);                 // 唯一启用超管
        var disabled = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true, enable: false);

        await svc.RevokeAsync(disabled);   // 不触发 E-SEC-037（目标 Enable=false）

        var u = await db.Sys_Users.IgnoreQueryFilters().FirstAsync(x => x.Id == disabled);
        Assert.False(u.IsPlatformAdmin);
    }

    // ── 撤不存在用户 → E-SEC-032。
    [Fact]
    public async Task Revoke_AbsentUser_Throws_ESec032()
    {
        var (svc, _) = Make();
        Assert.Equal("E-SEC-032",
            (await Assert.ThrowsAsync<InvalidOperationException>(() => svc.RevokeAsync(Guid.NewGuid()))).Message);
    }

    // ── 授予审计行（PlatformAdminGranted=23）落平台租户（R5），即便当前上下文为非默认租户。
    [Fact]
    public async Task Grant_Audit_LandsOnPlatformTenant()
    {
        var (svc, db) = Make(currentTenant: SomeOtherTenant);
        var uid = SeedUser(db, SomeOtherTenant);

        await svc.GrantAsync(uid);

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.PlatformAdminGranted);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }

    // ── 撤销审计行（PlatformAdminRevoked=24）落平台租户（R5）。
    [Fact]
    public async Task Revoke_Audit_LandsOnPlatformTenant()
    {
        var (svc, db) = Make(currentTenant: SomeOtherTenant);
        // 两个启用超管，撤其一可成功并审计。
        var a = SeedUser(db, SomeOtherTenant, isPlatformAdmin: true);
        SeedUser(db, SomeOtherTenant, isPlatformAdmin: true);

        await svc.RevokeAsync(a);

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.PlatformAdminRevoked);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }
}
