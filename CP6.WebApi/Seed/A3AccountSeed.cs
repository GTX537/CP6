using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>A3 固定资产科目对账（既有库幂等补全，spec §9）：补 Role 到 1601/1602/4301，新增 1606/1901/6115/6711。
/// 新装库已由 FinCoaTemplate 带出，此处仅修存量（CoA 未导入则跳过）。幂等可重入。</summary>
public static class A3AccountSeed
{
    private record Spec(string Code, string Name, AccountType Type, AccountSide Side, string Role);

    private static readonly Spec[] Items =
    {
        new("1601", "固定资产",       AccountType.Asset,   AccountSide.Debit,  "FIXED_ASSET"),
        new("1602", "累计折旧",       AccountType.Asset,   AccountSide.Credit, "ACCUM_DEPREC"),
        new("1606", "固定资产清理",   AccountType.Asset,   AccountSide.Debit,  "ASSET_CLEARING"),
        new("1901", "待处理财产损溢", AccountType.Asset,   AccountSide.Debit,  "PENDING_PROPERTY_LOSS"),
        new("6115", "资产处置损益",   AccountType.Expense, AccountSide.Debit,  "ASSET_DISPOSAL_PL"),
        new("6711", "营业外支出",     AccountType.Expense, AccountSide.Debit,  "NON_OP_EXPENSE"),
        new("4301", "营业外收入",     AccountType.Revenue, AccountSide.Credit, "NON_OP_INCOME"),
    };

    public static async Task EnsureAsync(CP6Context db)
    {
        if (!await db.GlAccounts.AnyAsync()) return;   // 空库未导模板 → 跳过
        foreach (var s in Items)
        {
            var acc = await db.GlAccounts.FirstOrDefaultAsync(a => a.Code == s.Code);
            if (acc == null)
                db.GlAccounts.Add(new GlAccount
                {
                    Id = Guid.NewGuid(), Code = s.Code, Name = s.Name,
                    Type = s.Type, NormalSide = s.Side, IsLeaf = true, Level = 1,
                    Role = s.Role, IsActive = true,
                });
            else if (string.IsNullOrEmpty(acc.Role))
                acc.Role = s.Role;
        }
        await db.SaveChangesAsync();
    }
}
