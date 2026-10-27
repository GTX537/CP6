using CP6.Core.EFDbContext;
using CP6.Core.Services.Oa;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>Registers action names only. No automatic role grants or business Owner registrations.</summary>
public static class ApprovalBindingPermissionSeed
{
    public static void EnsureSeeded(CP6Context db)
    {
        if (!db.Database.IsNpgsql()) return;
        var menu = db.Sys_Menus.SingleOrDefault(x => x.MenuKey == ApprovalBindingOwnerV2.Menu);
        if (menu == null) return;
        foreach (var tenant in db.Sys_Tenants.Select(x => x.Id).ToArray())
        foreach (var action in new[] { (ApprovalBindingOwnerV2.ReadAction, "审批绑定读取"), (ApprovalBindingOwnerV2.ConfigureAction, "审批绑定配置") })
            if (!db.Sys_MenuActions.IgnoreQueryFilters().Any(x => x.TenantId == tenant && x.MenuId == menu.MenuId && x.ActionCode == action.Item1))
                db.Sys_MenuActions.Add(new() { TenantId = tenant, MenuId = menu.MenuId, ActionCode = action.Item1, ActionName = action.Item2, Sort = 0 });
        db.SaveChanges();
    }
}
