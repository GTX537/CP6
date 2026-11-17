using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

public static class FinCostViewSeed
{
    public static void EnsureSeeded(CP6Context db)
    {
        var targets = (from tenant in db.Sys_Tenants
            from menu in db.Sys_Menus
            where menu.MenuKey == "fin-cost"
            select new { TenantId = tenant.Id, menu.MenuId }).ToList();
        var actions = new[]
        {
            ("view", "查看成本单"), ("rate-draft", "制订费率"), ("rate-review", "专业复核"), ("rate-release", "发布费率"),
            ("qualification-admin", "签发或撤销财务专业资格"), ("center-submit", "登记工序成本中心版本"),
            ("center-review", "复核工序成本中心版本"), ("center-release", "发布工序成本中心版本"),
        };
        foreach (var (code, name) in actions)
        {
            var existing = db.Sys_MenuActions.IgnoreQueryFilters().Where(a => a.ActionCode == code)
                .Select(a => new { a.TenantId, a.MenuId }).ToHashSet();
            foreach (var entry in targets.Where(e => !existing.Contains(e)))
                db.Sys_MenuActions.Add(new() { TenantId = entry.TenantId, MenuId = entry.MenuId, ActionCode = code, ActionName = name });
        }
        db.SaveChanges();
    }
}
