using CP6.Core.EFDbContext;
using CP6.Entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Tenant;

/// <summary>
/// 章10 §8（C-4）复合唯一索引补 TenantId 前缀的回归守门：检查 EF 模型元数据，
/// 断言每个 BaseTenantEntity 的唯一索引第一列都是 TenantId（防漏命门——新增租户表/唯一索引若未带前缀会被本测拦下）。
/// 共享表（Sys_Menu/Sys_Tenant 等非 BaseTenantEntity）不在约束内。
/// </summary>
public class TenantUniqueIndexTests
{
    [Fact]
    public void EveryTenantEntity_UniqueIndex_LeadsWithTenantId()
    {
        using var db = new CP6Context(new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        var offenders = new List<string>();
        foreach (var et in db.Model.GetEntityTypes())
        {
            if (!typeof(BaseTenantEntity).IsAssignableFrom(et.ClrType)) continue;

            // 被 FK 作为主键引用的唯一索引（父级业务编码）按设计保持全局唯一，不在约束内（见 CP6Context 重写循环注释）
            var fkPrincipalKeyProps = et.GetReferencingForeignKeys()
                .Select(fk => fk.PrincipalKey.Properties)
                .ToList();

            foreach (var idx in et.GetIndexes().Where(i => i.IsUnique))
            {
                if (fkPrincipalKeyProps.Any(kp => kp.SequenceEqual(idx.Properties))) continue;

                // S 类认证加固 T4：Sys_RefreshToken.TokenHash 按设计保持单列全局唯一——refresh 时无租户
                // 上下文（cookie 仅携带不可逆 hash），按 TokenHash + IgnoreQueryFilters 跨租户精确命中。
                // 与 CP6Context 唯一索引前缀循环的跳过条件镜像，故同样豁免本守卫。
                if (et.ClrType == typeof(CP6.Entity.DomainModels.Sys.Sys_RefreshToken)
                    && idx.Properties.Count == 1
                    && idx.Properties[0].Name == nameof(CP6.Entity.DomainModels.Sys.Sys_RefreshToken.TokenHash))
                    continue;

                if (idx.Properties.Count == 0 || idx.Properties[0].Name != nameof(BaseTenantEntity.TenantId))
                    offenders.Add($"{et.ClrType.Name}: ({string.Join(",", idx.Properties.Select(p => p.Name))})");
            }
        }

        Assert.True(offenders.Count == 0,
            "以下租户表唯一索引未以 TenantId 打头（需补复合前缀）: " + string.Join(" | ", offenders));
    }
}
