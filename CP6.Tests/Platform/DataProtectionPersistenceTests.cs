using CP6.Core.EFDbContext;
using CP6.Entity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory.Infrastructure.Internal;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Platform;

/// <summary>
/// P0-T1：DataProtection 密钥环持久化到数据库（EF）单测。
/// 覆盖：①密钥落 DataProtectionKeys 表（启动后至少一行）；②Protect/Unprotect 往返；
/// ③新建第二个 ServiceProvider（模拟进程重启，共享同一 DB）能解密第一个的密文；
/// ④回归护栏：DataProtectionKey 非 BaseTenantEntity，不被反射租户过滤误伤。
/// </summary>
public class DataProtectionPersistenceTests
{
    /// <summary>共享 InMemory 根 → 多个 ServiceProvider（内部 EF 容器不同）仍读到同一份数据。</summary>
    private static ServiceProvider BuildProvider(InMemoryDatabaseRoot root, string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<CP6Context>(o => o.UseInMemoryDatabase(dbName, root));
        services.AddDataProtection()
            .PersistKeysToDbContext<CP6Context>()
            .SetApplicationName("CP6");
        return services.BuildServiceProvider();
    }

    [Fact]
    public void CP6Context_Implements_IDataProtectionKeyContext()
    {
        // 契约：DbContext 必须实现 EF 密钥仓接口，PersistKeysToDbContext 才能编译/工作
        Assert.True(typeof(IDataProtectionKeyContext).IsAssignableFrom(typeof(CP6Context)));
    }

    [Fact]
    public void DataProtectionKey_Is_Not_TenantFiltered()
    {
        // 回归护栏：DataProtectionKey 不是 BaseTenantEntity，反射租户过滤（CP6Context 只扫 BaseTenantEntity 子类）不得误伤
        Assert.False(typeof(BaseTenantEntity).IsAssignableFrom(typeof(DataProtectionKey)));

        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new CP6Context(options);
        var et = db.Model.FindEntityType(typeof(DataProtectionKey));
        Assert.NotNull(et);                       // 已作为实体映射
        Assert.Null(et!.GetQueryFilter());        // 无全局查询过滤
    }

    [Fact]
    public void Keys_Are_Persisted_To_Table_After_First_Protect()
    {
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildProvider(root, dbName);

        var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("p0t1.test");
        var cipher = protector.Protect("client-secret");   // 首次加密触发密钥生成 + 落库

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CP6Context>();
        Assert.True(db.DataProtectionKeys.Any(), "首次 Protect 后 DataProtectionKeys 表应至少有一行");
        Assert.NotEqual("client-secret", cipher);          // 确实加密了
    }

    [Fact]
    public void Roundtrip_Protect_Unprotect_Same_Provider()
    {
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();
        using var sp = BuildProvider(root, dbName);

        var protector = sp.GetRequiredService<IDataProtectionProvider>().CreateProtector("p0t1.test");
        var cipher = protector.Protect("hello-cp6");
        Assert.Equal("hello-cp6", protector.Unprotect(cipher));
    }

    [Fact]
    public void SecondProvider_SharedDb_Can_Decrypt_FirstProviders_Ciphertext()
    {
        // 模拟进程重启：同一 DB（共享 InMemory 根），第二个 ServiceProvider 用持久化的密钥环解密旧密文
        var root = new InMemoryDatabaseRoot();
        var dbName = Guid.NewGuid().ToString();

        string cipher;
        using (var sp1 = BuildProvider(root, dbName))
        {
            var p1 = sp1.GetRequiredService<IDataProtectionProvider>().CreateProtector("p0t1.test");
            cipher = p1.Protect("survive-restart");
        }

        using var sp2 = BuildProvider(root, dbName);
        var p2 = sp2.GetRequiredService<IDataProtectionProvider>().CreateProtector("p0t1.test");
        Assert.Equal("survive-restart", p2.Unprotect(cipher));
    }
}
