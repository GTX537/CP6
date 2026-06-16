using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Tenant;

/// <summary>
/// 章10 §7 租户注册表（Sys_Tenant）+ 默认租户种子 + 活跃租户枚举。验证：
/// seed 幂等只建一行默认租户 / 枚举只取启用租户 / 空表回退默认租户（后台至少跑一遍）。
/// </summary>
public class TenantRegistryTests
{
    private static CP6Context Db(string dbName) => new(
        new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(dbName)
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    [Fact]
    public void Seed_DefaultTenant_IsIdempotent()
    {
        var name = Guid.NewGuid().ToString();
        using (var db = Db(name)) Assert.True(TenantSeed.EnsureSeeded(db));   // 首次新增
        using (var db = Db(name)) Assert.False(TenantSeed.EnsureSeeded(db));  // 再播不重复

        using (var db = Db(name))
        {
            var rows = db.Sys_Tenants.ToList();
            Assert.Single(rows);
            Assert.Equal(TenantContext.DefaultTenant, rows[0].Id);
            Assert.True(rows[0].Enable);
        }
    }

    [Fact]
    public async Task Enumerator_ReturnsOnlyEnabledTenants()
    {
        var name = Guid.NewGuid().ToString();
        var active = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        using (var db = Db(name))
        {
            db.Sys_Tenants.AddRange(
                new Sys_Tenant { Id = active, TenantCode = "A", TenantName = "a", Enable = true },
                new Sys_Tenant { Id = disabled, TenantCode = "B", TenantName = "b", Enable = false });
            await db.SaveChangesAsync();
        }

        using (var db = Db(name))
        {
            var ids = await new TenantEnumerator(db).ListActiveAsync();
            Assert.Single(ids);
            Assert.Contains(active, ids);
            Assert.DoesNotContain(disabled, ids);
        }
    }

    [Fact]
    public async Task Enumerator_EmptyTable_FallsBackToDefaultTenant()
    {
        using var db = Db(Guid.NewGuid().ToString());
        var ids = await new TenantEnumerator(db).ListActiveAsync();
        Assert.Single(ids);
        Assert.Equal(TenantContext.DefaultTenant, ids[0]);
    }
}
