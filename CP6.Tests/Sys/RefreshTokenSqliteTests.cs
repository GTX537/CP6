using CP6.Entity;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// T4 plan Step 4：刷新令牌「TokenHash 单列全局唯一」+「跨租户按 TokenHash 命中」。
///
/// 关于 SQLite：CP6Context 含 nvarchar(max) 列（BaseTenantEntity.Creator 等），与 SQLite
/// EnsureCreated 不兼容（同 A3 H-1 / A4 H-2 / A5 已记录限制），无法用 SQLite harness 建表。
/// 但本 Task 要保的两点不依赖真库引擎：
///   ① 唯一索引「单列、未被 TenantId 前缀」是 <b>模型级</b>事实——直接断言 EF 模型即可，
///      比 SQLite 建表更直接地证明 CP6Context 的唯一索引前缀循环对 TokenHash 的跳过条件生效；
///   ② 「默认租户上下文下仍能按 TokenHash 命中他租令牌并回设 TenantId」是 IgnoreQueryFilters
///      行为，InMemory + ChangeTracker.Clear()（强制真实重载，绕开 EF 导航修复掩盖）即可真验。
/// 故不写 SQLite 红测试，改以模型断言 + 跨租户 InMemory 覆盖，结论强度等价或更高。
/// </summary>
public class RefreshTokenSqliteTests
{
    private static CP6Context DbFor(string dbName, Guid tenant) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options,
        new TenantContext { CurrentTenantId = tenant });

    [Fact]
    public void TokenHash_unique_index_is_single_column_not_tenant_prefixed()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var et = db.Model.FindEntityType(typeof(Sys_RefreshToken))!;

        var unique = et.GetIndexes().Single(i => i.IsUnique);
        // 跳过条件生效：仍是单列 TokenHash，未被升级为 (TenantId, TokenHash)
        Assert.Single(unique.Properties);
        Assert.Equal(nameof(Sys_RefreshToken.TokenHash), unique.Properties[0].Name);
        Assert.NotEqual(nameof(BaseTenantEntity.TenantId), unique.Properties[0].Name);
    }

    [Fact]
    public async Task Rotate_finds_token_across_tenants_under_default_context()
    {
        var dbName = Guid.NewGuid().ToString();
        var tenantB = Guid.NewGuid();
        Guid userId;
        string raw;

        // 在租户 B 上下文签发令牌（盖章 TenantId = B）
        using (var ctxB = DbFor(dbName, tenantB))
        {
            var user = new Sys_User { UserName = "b-user", Password = "h" };
            ctxB.Sys_Users.Add(user);
            await ctxB.SaveChangesAsync();
            userId = user.Id;
            var svcB = new RefreshTokenService(ctxB, Options.Create(new SecurityOptions()), new TenantContext { CurrentTenantId = tenantB });
            raw = await svcB.IssueAsync(user, null, null);
        }

        // 默认租户上下文（refresh 无 B 的上下文）仍按 TokenHash 命中 B 的令牌并回设 TenantId
        using (var ctxDefault = DbFor(dbName, TenantContext.DefaultTenant))
        {
            ctxDefault.ChangeTracker.Clear();   // 强制真实重载，绕开导航修复掩盖
            var tenantCtx = new TenantContext { CurrentTenantId = TenantContext.DefaultTenant };
            var svc = new RefreshTokenService(ctxDefault, Options.Create(new SecurityOptions()), tenantCtx);

            var (newRaw, who) = await svc.RotateAsync(raw, null, null);

            Assert.Equal(userId, who.Id);
            Assert.NotEqual(raw, newRaw);
            Assert.Equal(tenantB, tenantCtx.CurrentTenantId);   // 由令牌回设为 B
            // 新令牌盖在 B 租户下
            var newRow = ctxDefault.Sys_RefreshTokens.IgnoreQueryFilters().Single(r => r.RevokedAt == null);
            Assert.Equal(tenantB, newRow.TenantId);
        }
    }
}
