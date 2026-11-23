using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

internal static class WorkflowForecastAuthorityV1
{
    internal const string Action = "conn.forecast";

    internal static async Task RequireAsync(CP6Context db, UserPermissionContext actor, CancellationToken ct)
    {
        const string menu = ConnectorVersionControlService.Menu;
        var registrations = await (from action in db.Sys_MenuActions
                                   join row in db.Sys_Menus on action.MenuId equals row.MenuId
                                   where row.Enable && row.MenuKey == menu && action.ActionCode == Action
                                   select action.MenuId).Take(2).CountAsync(ct);
        // 仅消费已登记的精确 Owner 动作。目录缺失或歧义时交 Owner 裁定。
        if (registrations != 1)
            throw new ScopeAuthorizationExceptionV1("FORECAST_AUTHORITY_UNPROVEN", 503);
        if (!actor.MenuKeys.Contains(menu) || !actor.ActionKeys.Contains(menu + ":" + Action))
            throw new ScopeAuthorizationExceptionV1("FORECAST_ACTION_FORBIDDEN", 403);
    }
}
