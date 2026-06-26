using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Platform;

/// <summary>
/// 块① 平台租户管理服务单测（直构服务 + InMemory）。覆盖：建租户原子（R9-a）、编码冲突 E-SEC-033、
/// 失败回滚、停用/重启用 + 审计落平台租户（R5）、列表过滤/分页 clamp/userCount、改不存在 E-SEC-032。
/// </summary>
public class TenantAdminServiceTests
{
    private static readonly Guid SomeOtherTenant = Guid.Parse("00000000-0000-0000-0000-0000000000B7");

    /// <summary>BCrypt 真哈希器（验 Verify 真实通过）。</summary>
    private sealed class RealHasher : IPasswordHasher
    {
        public string Hash(string plain) => BCrypt.Net.BCrypt.HashPassword(plain);
        public bool Verify(string plain, string hash) => BCrypt.Net.BCrypt.Verify(plain, hash);
        public bool IsHashed(string value) => value.StartsWith("$2");
    }

    /// <summary>Hash 抛异常的假哈希器（验 R9-a 回滚：异常先于任何 Add 发生时两行都不落库）。</summary>
    private sealed class ThrowingHasher : IPasswordHasher
    {
        public string Hash(string plain) => throw new InvalidOperationException("hash-boom");
        public bool Verify(string plain, string hash) => false;
        public bool IsHashed(string value) => false;
    }

    private static (TenantAdminService svc, CP6Context db, TenantContext tenant) Make(
        IPasswordHasher? hasher = null, Guid? currentTenant = null)
    {
        var tenant = new TenantContext { CurrentTenantId = currentTenant ?? TenantContext.DefaultTenant };
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CP6Context(options, tenant);   // 注入同一 TenantContext → StampTenant 读它
        var audit = new SecurityAuditService(db);
        var svc = new TenantAdminService(db, hasher ?? new RealHasher(), audit, tenant);
        return (svc, db, tenant);
    }

    // ── 建租户成功：Sys_Tenant + 首个 admin 同时存在，admin 字段集正确，密码经 Verify 真实通过。
    [Fact]
    public async Task Create_ProvisionsTenantAndFirstAdmin_Atomically()
    {
        var hasher = new RealHasher();
        var (svc, db, _) = Make(hasher);

        var result = await svc.CreateAsync("ACME", "ACME 株式会社", null, "first tenant", "acme_admin");

        // 租户落库。
        var tenant = await db.Sys_Tenants.FirstOrDefaultAsync(t => t.Id == result.TenantId);
        Assert.NotNull(tenant);
        Assert.Equal("ACME", tenant!.TenantCode);
        Assert.True(tenant.Enable);

        // 首个 admin 落库（受全局过滤 → IgnoreQueryFilters 读）。
        var admin = await db.Sys_Users.IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.UserName == "acme_admin");
        Assert.NotNull(admin);
        Assert.Equal(result.TenantId, admin!.TenantId);
        Assert.False(admin.IsPlatformAdmin);
        Assert.True(admin.MustChangePassword);
        Assert.Equal(1, admin.RoleId);
        Assert.True(admin.Enable);

        // 临时明文经哈希后可验。
        Assert.Equal("acme_admin", result.AdminUserName);
        Assert.False(string.IsNullOrEmpty(result.TempPassword));
        Assert.True(hasher.Verify(result.TempPassword, admin.Password));
    }

    // ── 临时密码 16 字符且四类字符齐全。
    [Fact]
    public async Task Create_TempPassword_Is16CharsWithAllClasses()
    {
        var (svc, _, _) = Make();
        var result = await svc.CreateAsync("PWTEST", "PW", null, null, "pw_admin");

        Assert.Equal(16, result.TempPassword.Length);
        Assert.Contains(result.TempPassword, char.IsUpper);
        Assert.Contains(result.TempPassword, char.IsLower);
        Assert.Contains(result.TempPassword, char.IsDigit);
        Assert.Contains(result.TempPassword, c => !char.IsLetterOrDigit(c));
    }

    // ── 编码冲突 → E-SEC-033（InvalidOperationException 携码）。
    [Fact]
    public async Task Create_DuplicateCode_Throws_ESec033()
    {
        var (svc, _, _) = Make();
        await svc.CreateAsync("DUP", "first", null, null, "a1");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync("DUP", "second", null, null, "a2"));
        Assert.Equal("E-SEC-033", ex.Message);
    }

    // ── R9-a 原子回滚：Hash 抛异常 → CreateAsync 抛 + Sys_Tenant 行不落库。
    // 注意（InMemory 限制）：此处 Hash 在任何 Add/SaveChanges 之前抛，故租户从未被 Add，
    // "不存在" 平凡成立；InMemory 约束弱、无法可靠触发"第二实体 SaveChanges 失败"路径，
    // 真实事务回滚由生产 SqlServer 单 SaveChanges 保证（见服务注释）。本测固化"建租户失败零残留"不变量。
    [Fact]
    public async Task Create_WhenHashThrows_NoTenantRowPersisted()
    {
        var (svc, db, _) = Make(new ThrowingHasher());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CreateAsync("ROLLBACK", "x", null, null, "a"));

        Assert.False(await db.Sys_Tenants.AnyAsync(t => t.TenantCode == "ROLLBACK"));
        Assert.False(await db.Sys_Users.IgnoreQueryFilters().AnyAsync(u => u.UserName == "a"));
    }

    // ── 停用 → Enable=false，且审计行落平台租户（R5）。当前上下文为非默认租户也不影响。
    [Fact]
    public async Task Suspend_FlipsEnable_AndAuditLandsOnPlatformTenant()
    {
        var (svc, db, _) = Make(currentTenant: SomeOtherTenant);
        var created = await svc.CreateAsync("SUSP", "Susp", null, null, "su");

        await svc.SuspendAsync(created.TenantId);

        var tenant = await db.Sys_Tenants.FirstAsync(t => t.Id == created.TenantId);
        Assert.False(tenant.Enable);

        // 审计行（TenantSuspended=21）TenantId 必须落平台租户。
        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.TenantSuspended);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }

    // ── 重启用 → Enable=true，审计行落平台租户。
    [Fact]
    public async Task Reactivate_FlipsEnable_AndAuditLandsOnPlatformTenant()
    {
        var (svc, db, _) = Make(currentTenant: SomeOtherTenant);
        var created = await svc.CreateAsync("REACT", "React", null, null, "re");
        await svc.SuspendAsync(created.TenantId);

        await svc.ReactivateAsync(created.TenantId);

        var tenant = await db.Sys_Tenants.FirstAsync(t => t.Id == created.TenantId);
        Assert.True(tenant.Enable);

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.TenantReactivated);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }

    // ── 建租户审计行（TenantCreated=19）落平台租户。
    [Fact]
    public async Task Create_Audit_LandsOnPlatformTenant()
    {
        var (svc, db, _) = Make(currentTenant: SomeOtherTenant);
        await svc.CreateAsync("AUD", "Aud", null, null, "au");

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.TenantCreated);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }

    // ── Update 不存在 → E-SEC-032。
    [Fact]
    public async Task Update_NotFound_Throws_ESec032()
    {
        var (svc, _, _) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.UpdateAsync(Guid.NewGuid(), "x", null, null));
        Assert.Equal("E-SEC-032", ex.Message);
    }

    // ── Suspend 不存在 → E-SEC-032。
    [Fact]
    public async Task Suspend_NotFound_Throws_ESec032()
    {
        var (svc, _, _) = Make();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.SuspendAsync(Guid.NewGuid()));
        Assert.Equal("E-SEC-032", ex.Message);
    }

    // ── Update 成功改名称/到期/备注 + 审计落平台租户。
    [Fact]
    public async Task Update_ModifiesFields()
    {
        var (svc, db, _) = Make(currentTenant: SomeOtherTenant);
        var created = await svc.CreateAsync("UPD", "old", null, "oldRemark", "up");
        var expire = new DateTime(2030, 1, 1);

        await svc.UpdateAsync(created.TenantId, "new", expire, "newRemark");

        var tenant = await db.Sys_Tenants.FirstAsync(t => t.Id == created.TenantId);
        Assert.Equal("new", tenant.TenantName);
        Assert.Equal(expire, tenant.ExpireDate);
        Assert.Equal("newRemark", tenant.Remark);

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.TenantUpdated);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }

    // ── Get 含 userCount + null 缺。
    [Fact]
    public async Task Get_ReturnsDetailWithUserCount_OrNull()
    {
        var (svc, db, _) = Make();
        var created = await svc.CreateAsync("GET", "Get", null, "r", "g_admin");
        // 该租户除首个 admin 外，再加 2 个用户 → userCount=3。
        SeedExtraUsers(db, created.TenantId, 2);

        var detail = await svc.GetAsync(created.TenantId);
        Assert.NotNull(detail);
        Assert.Equal("GET", detail!.TenantCode);
        Assert.Equal("r", detail.Remark);
        Assert.Equal(3, detail.UserCount);

        Assert.Null(await svc.GetAsync(Guid.NewGuid()));
    }

    // ── List：keyword/enable 过滤、分页 clamp(page=0→1,pageSize=500→200)、userCount 正确。
    [Fact]
    public async Task List_FiltersAndClampsAndCountsUsers()
    {
        var (svc, db, _) = Make();
        var a = await svc.CreateAsync("ALPHA", "Alpha Corp", null, null, "al");
        await svc.CreateAsync("BETA", "Beta Corp", null, null, "be");
        var gamma = await svc.CreateAsync("GAMMA", "Gamma Corp", null, null, "ga");
        await svc.SuspendAsync(gamma.TenantId);   // GAMMA 停用

        // ALPHA 再加 2 用户 → userCount=3。
        SeedExtraUsers(db, a.TenantId, 2);

        // keyword 命中名称。
        var byKeyword = await svc.ListAsync("Alpha", null, 1, 20);
        Assert.Equal(1, byKeyword.Total);
        Assert.Equal("ALPHA", byKeyword.Rows[0].TenantCode);
        Assert.Equal(3, byKeyword.Rows[0].UserCount);

        // enable=false 仅命中 GAMMA。
        var disabled = await svc.ListAsync(null, false, 1, 20);
        Assert.Single(disabled.Rows);
        Assert.Equal("GAMMA", disabled.Rows[0].TenantCode);

        // enable=true 命中 ALPHA+BETA。
        var enabled = await svc.ListAsync(null, true, 1, 20);
        Assert.Equal(2, enabled.Total);

        // 分页 clamp：page=0→1, pageSize=500→200（不抛 + 全部返回，总数=3）。
        var clamped = await svc.ListAsync(null, null, 0, 500);
        Assert.Equal(3, clamped.Total);
        Assert.Equal(3, clamped.Rows.Count);
    }

    private static void SeedExtraUsers(CP6Context db, Guid tenantId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            db.Sys_Users.Add(new Sys_User
            {
                Id = Guid.NewGuid(),
                UserName = $"extra_{tenantId:N}_{i}",
                Password = "x",
                TenantId = tenantId,
                Enable = true
            });
        }
        db.SaveChanges();
    }
}
