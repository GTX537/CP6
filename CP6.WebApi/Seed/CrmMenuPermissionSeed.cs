using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// CRM menu/action catalogue. Menus remain disabled until the corresponding Vue screens land;
/// action definitions and administrator grants are tenant-idempotent from the foundation release.
/// </summary>
public static class CrmMenuPermissionSeed
{
    private static readonly (int Id, string Name, string? Route, string? Key, string Icon, int? Parent, int Order)[] Menus =
    {
        (800, "客户关系管理(CRM)", null, null, "UserFilled", null, 800),
        (801, "CRM 工作台", "/crm/dashboard", "crm-dashboard", "DataAnalysis", 800, 801),
        (802, "线索管理", "/crm/leads", "crm-lead", "Connection", 800, 802),
        (803, "企业与联系人", "/crm/accounts", "crm-account", "OfficeBuilding", 800, 803),
        (804, "商机管理", "/crm/opportunities", "crm-opportunity", "TrendCharts", 800, 804),
        (805, "营销官网", "/crm/site", "crm-site", "Promotion", 800, 805),
    };

    private static readonly (int MenuId, string Code, string Name)[] Actions =
    {
        (801, "query", "查看工作台"),
        (802, "query", "查看线索"), (802, "add", "新增线索"), (802, "edit", "编辑线索"),
        (802, "assign", "分配/移交"), (802, "merge", "合并线索"), (802, "convert", "转换商机"),
        (802, "view-pii", "查看个人信息"),
        (803, "query", "查看企业联系人"), (803, "add", "新增企业联系人"),
        (803, "edit", "编辑企业联系人"), (803, "view-pii", "查看个人信息"),
        (804, "query", "查看商机"), (804, "add", "新增商机"), (804, "edit", "编辑商机"),
        (804, "accept-quote", "登记报价接受"), (804, "create-order", "创建ERP订单"),
        (804, "view-pii", "查看个人信息"),
        (805, "query", "查看官网内容"), (805, "edit", "编辑官网内容"),
        (805, "publish", "发布/回滚官网"), (805, "configure", "配置官网与表单"),
    };

    public static void EnsureSeeded(CP6Context db)
    {
        var changed = false;
        foreach (var (id, name, route, key, icon, parent, order) in Menus)
        {
            var menu = db.Sys_Menus.SingleOrDefault(x => x.MenuId == id);
            if (menu == null)
            {
                db.Sys_Menus.Add(new Sys_Menu
                {
                    MenuId = id,
                    MenuName = name,
                    RoutePath = route,
                    MenuKey = key,
                    Icon = icon,
                    ParentId = parent,
                    OrderNo = order,
                    Enable = false,
                });
                changed = true;
                continue;
            }

            // Correct stable identifiers without re-disabling screens enabled by a later delivery.
            if (menu.MenuName != name || menu.RoutePath != route || menu.MenuKey != key ||
                menu.Icon != icon || menu.ParentId != parent || menu.OrderNo != order)
            {
                menu.MenuName = name;
                menu.RoutePath = route;
                menu.MenuKey = key;
                menu.Icon = icon;
                menu.ParentId = parent;
                menu.OrderNo = order;
                changed = true;
            }
        }

        if (changed)
        {
            db.SaveChanges();
            changed = false;
        }

        var tenantIds = db.Sys_Tenants.Select(x => x.Id).ToList();
        foreach (var tenantId in tenantIds)
        {
            foreach (var menuId in Menus.Select(x => x.Id))
            {
                if (!db.Sys_RoleMenus.IgnoreQueryFilters().Any(x =>
                        x.TenantId == tenantId && x.RoleId == 1 && x.MenuId == menuId))
                {
                    db.Sys_RoleMenus.Add(new Sys_RoleMenu { TenantId = tenantId, RoleId = 1, MenuId = menuId });
                    changed = true;
                }
            }

            foreach (var (menuId, code, name) in Actions)
            {
                if (!db.Sys_MenuActions.IgnoreQueryFilters().Any(x =>
                        x.TenantId == tenantId && x.MenuId == menuId && x.ActionCode == code))
                {
                    db.Sys_MenuActions.Add(new Sys_MenuAction
                    {
                        TenantId = tenantId,
                        MenuId = menuId,
                        ActionCode = code,
                        ActionName = name,
                    });
                    changed = true;
                }

                if (!db.Sys_RoleActions.IgnoreQueryFilters().Any(x =>
                        x.TenantId == tenantId && x.RoleId == 1 && x.MenuId == menuId && x.ActionCode == code))
                {
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tenantId,
                        RoleId = 1,
                        MenuId = menuId,
                        ActionCode = code,
                    });
                    changed = true;
                }
            }
        }

        if (changed) db.SaveChanges();
    }
}
