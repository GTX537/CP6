using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

public static class FinCostViewSeed
{
    public static void EnsureSeeded(CP6Context db)
    {
        var missing = from tenant in db.Sys_Tenants
            from menu in db.Sys_Menus
            where menu.MenuKey == "fin-cost" && !db.Sys_MenuActions.IgnoreQueryFilters().Any(action =>
                action.MenuId == menu.MenuId && action.TenantId == tenant.Id && action.ActionCode == "view")
            select new { TenantId = tenant.Id, menu.MenuId };
        foreach (var entry in missing.ToList())
            db.Sys_MenuActions.Add(new()
            {
                TenantId = entry.TenantId,
                MenuId = entry.MenuId,
                ActionCode = "view",
                ActionName = "查看成本单",
            });
        db.SaveChanges();
    }
}
