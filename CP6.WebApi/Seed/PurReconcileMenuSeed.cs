using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;

namespace CP6.WebApi.Seed;

/// <summary>Seeds the procurement reconciliation menu with its resource key on the first initialization.</summary>
public static class PurReconcileMenuSeed
{
    public static void EnsureSeeded(CP6Context db)
    {
        if (!db.Sys_Menus.Any(m => m.MenuId == 708))
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = 708,
                MenuName = "采购对账",
                RoutePath = "/pur/reconcile",
                MenuKey = "pur-reconcile",
                Icon = "Finished",
                ParentId = 700,
                OrderNo = 708,
                Enable = true,
            });
            db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = 1, MenuId = 708 });
            db.SaveChanges();
        }
    }
}
