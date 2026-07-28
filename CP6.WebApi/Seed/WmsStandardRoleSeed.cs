using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Seed;

/// <summary>
/// Insert-only standard warehouse roles. Existing tenant customization is
/// preserved; the seed only fills missing role/menu/action rows.
/// </summary>
public static class WmsStandardRoleSeed
{
    public const int SupervisorRoleId = 20;
    public const int DispatcherRoleId = 21;
    public const int OperatorRoleId = 22;
    public const int AuditorRoleId = 23;
    private const int MobileMenuId = 461;

    private static readonly (int id, string name, string[] actions)[] Roles =
    {
        (SupervisorRoleId, "仓库主管", new[]
        {
            "view", "add", "assign", "claim", "start", "scan", "complete",
            "cancel", "pause", "release", "takeover", "exception",
            "barcode-manage", "device-manage", "analytics", "serial-manage",
            "lpn-manage", "label-manage", "label-print"
        }),
        (DispatcherRoleId, "调度员", new[]
        {
            "view", "add", "assign", "cancel", "pause", "release",
            "exception", "barcode-manage", "serial-manage", "lpn-manage",
            "label-manage", "label-print"
        }),
        (OperatorRoleId, "作业员", new[]
        {
            "view", "claim", "start", "scan", "complete", "pause", "exception",
            "serial-manage", "lpn-manage", "label-print"
        }),
        (AuditorRoleId, "只读审计员", new[] { "view" })
    };

    public static void EnsureSeeded(CP6Context db)
    {
        var tenantIds = db.Sys_Tenants.Select(x => x.Id).ToList();
        var changed = false;
        foreach (var tenantId in tenantIds)
        {
            foreach (var (roleId, roleName, actions) in Roles)
            {
                if (!db.Sys_Roles.IgnoreQueryFilters()
                    .Any(x => x.TenantId == tenantId && x.RoleId == roleId))
                {
                    db.Sys_Roles.Add(new Sys_Role
                    {
                        TenantId = tenantId,
                        RoleId = roleId,
                        RoleName = roleName,
                        Description = "CP6 WMS standard role",
                        Enable = true,
                        OrderNo = roleId
                    });
                    changed = true;
                }
                if (!db.Sys_RoleMenus.IgnoreQueryFilters()
                    .Any(x => x.TenantId == tenantId
                              && x.RoleId == roleId
                              && x.MenuId == MobileMenuId))
                {
                    db.Sys_RoleMenus.Add(new Sys_RoleMenu
                    {
                        TenantId = tenantId,
                        RoleId = roleId,
                        MenuId = MobileMenuId
                    });
                    changed = true;
                }
                foreach (var action in actions)
                {
                    if (db.Sys_RoleActions.IgnoreQueryFilters()
                        .Any(x => x.TenantId == tenantId
                                  && x.RoleId == roleId
                                  && x.MenuId == MobileMenuId
                                  && x.ActionCode == action))
                        continue;
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tenantId,
                        RoleId = roleId,
                        MenuId = MobileMenuId,
                        ActionCode = action
                    });
                    changed = true;
                }
            }
        }
        if (changed) db.SaveChanges();
    }
}
