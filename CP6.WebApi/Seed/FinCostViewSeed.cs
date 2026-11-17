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
            ("basis-submit", "登记标准成本依据"), ("basis-review", "独立复核标准成本依据"),
            ("basis-release", "启用批准标准成本依据"), ("basis-revoke", "撤销标准成本依据"),
            ("basis-view", "查询标准成本依据及历史比较"), ("basis-use", "按批准标准依据归集"),
            ("cost-master-view", "查询成本政策和费率依据"),
            ("policy-submit", "登记成本政策"), ("policy-review", "复核成本政策"), ("policy-release", "发布成本政策"),
            ("rate-basis-submit", "登记费率依据"), ("rate-basis-review", "复核费率依据"), ("rate-basis-release", "发布费率依据"),
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
