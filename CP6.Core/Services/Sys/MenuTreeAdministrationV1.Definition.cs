using System.Text;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed partial class MenuTreeAdministrationV1
{
    public Task<MenuTreeImpactV1> PreviewDefinitionAsync(MenuDefinitionCommandV1 command,
        IReadOnlySet<string> routes, TenantManagementSessionV1 session, CancellationToken ct)
        => Transaction(false, session, (_, token) => DefinitionImpact(command, routes, token), ct);

    public Task<MenuTreeReceiptV1> ExecuteDefinitionAsync(MenuDefinitionCommandV1 command,
        IReadOnlySet<string> routes, TenantManagementSessionV1 session, CancellationToken ct)
    {
        Operation(command.OperationRef);
        ValidateText(command);
        var input = Bytes(command);
        return Transaction(true, session, async (actor, token) =>
        {
            await RequireDefinitionAction(command, token);
            var saved = await Original(command.OperationRef, token);
            if (saved != null) return Receipt(saved, actor, command.OperationRef, input);

            // 原生菜单写者也经由数据库根版本推进。持有根锁完成比较与写入。
            _ = await db.Database.SqlQueryRaw<long>(
                """SELECT revision AS "Value" FROM cp6_menu_tree_v1.root WHERE singleton FOR UPDATE""").SingleAsync(token);
            var impact = await DefinitionImpact(command, routes, token, actionChecked: true);
            if (impact.ChangedMenuIds.Length > 0)
                await ApplyDefinition(command, token);
            return await CompleteCommand(actor, command.OperationRef, input, command.Reason, "shared-menu-definition-v1", impact, token);
        }, ct);
    }

    private async Task<MenuTreeImpactV1> DefinitionImpact(MenuDefinitionCommandV1 command,
        IReadOnlySet<string> routes, CancellationToken ct, bool actionChecked = false)
    {
        if (!actionChecked)
        {
            ValidateText(command);
            await RequireDefinitionAction(command, ct);
        }
        var current = await Snapshot(ct);
        if (command.ExpectedRevision != current.Revision)
            throw new MenuTreeExceptionV1("MENU_TREE_VERSION_CHANGED", 412);
        var node = current.Menus.SingleOrDefault(x => x.MenuId == command.MenuId);
        if (command.Kind == "create" && node is not null)
            throw new MenuTreeExceptionV1("MENU_DEFINITION_ID_EXISTS");
        if (command.Kind != "create" && node is null)
            throw new MenuTreeExceptionV1("MENU_TREE_NODE_NOT_FOUND", 404);

        if (command.Kind == "delete")
        {
            await RequireDefinitionReferencesGone(db, [command.MenuId], checkChildren: true, ct);
            if (node!.Enable) throw new MenuTreeExceptionV1("MENU_DEFINITION_DISABLE_FIRST");
            return await References(current, [command.MenuId], ct);
        }

        var fields = command.Value!;
        if (fields.RoutePath != node?.RoutePath && fields.RoutePath is { } route && !routes.Contains(route))
            throw new MenuTreeExceptionV1("MENU_DEFINITION_ROUTE_UNKNOWN", 400);
        if (fields.MenuKey != node?.MenuKey)
        {
            if (fields.MenuKey is not null && current.Menus.Any(x => x.MenuId != command.MenuId &&
                string.Equals(x.MenuKey, fields.MenuKey, StringComparison.OrdinalIgnoreCase)))
                throw new MenuTreeExceptionV1("MENU_DEFINITION_KEY_EXISTS");
            if (node is not null && await HasRoleOrActionReference(db, [node.MenuId], ct))
                throw new MenuTreeExceptionV1("MENU_DEFINITION_KEY_REFERENCED");
        }

        var parents = current.Menus.ToDictionary(x => x.MenuId, x => x.ParentId);
        var visited = new HashSet<int> { command.MenuId };
        var parentId = fields.ParentId;
        while (parentId is { } parent)
        {
            if (!visited.Add(parent) || !parents.TryGetValue(parent, out parentId))
                throw new MenuTreeExceptionV1("MENU_TREE_PARENT_INVALID", 400);
        }

        var changed = node is null || fields != new MenuDefinitionFieldsV1(node.MenuName, node.RoutePath,
            node.MenuKey, node.Icon, node.ParentId, node.OrderNo, node.Enable);
        return await References(current, changed ? [command.MenuId] : [], ct);
    }

    private static void ValidateText(MenuDefinitionCommandV1 command)
    {
        if (command.Kind is not ("create" or "update" or "delete") || command.MenuId <= 0 || command.ExpectedRevision <= 0 ||
            string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256 || (command.Kind == "delete") != (command.Value is null))
            throw new MenuTreeExceptionV1("MENU_DEFINITION_INPUT_INVALID", 400);
        var fields = command.Value;
        if (fields is not null && (string.IsNullOrWhiteSpace(fields.MenuName) || fields.MenuName.Length > 100 ||
            fields.RoutePath?.Length > 200 || fields.Icon?.Length > 100 || fields.OrderNo < 0 ||
            fields.MenuKey is { } key && (key.Length is < 1 or > 100 ||
                key.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))))
            throw new MenuTreeExceptionV1("MENU_DEFINITION_INPUT_INVALID", 400);
        var utf8 = new UTF8Encoding(false, true);
        foreach (var text in new[] { command.Reason, fields?.MenuName, fields?.RoutePath, fields?.MenuKey, fields?.Icon })
        {
            if (text is null) continue;
            if (text.Contains('\0')) throw new MenuTreeExceptionV1("MENU_DEFINITION_INPUT_INVALID", 400);
            try { _ = utf8.GetByteCount(text); }
            catch (EncoderFallbackException error) { throw new MenuTreeExceptionV1("MENU_DEFINITION_INPUT_INVALID", 400, error); }
        }
    }

    private async Task RequireDefinitionAction(MenuDefinitionCommandV1 command, CancellationToken ct)
    {
        if (command.Kind == "update") return;
        ct.ThrowIfCancellationRequested();
        var action = command.Kind == "create" ? "menu:add" : "menu:delete";
        if (!(await permissions.GetAsync()).ActionKeys.Contains(action))
            throw new MenuTreeExceptionV1("MENU_TREE_FORBIDDEN", 403);
    }

    private async Task ApplyDefinition(MenuDefinitionCommandV1 command, CancellationToken ct)
    {
        var node = command.Kind == "create" ? new Sys_Menu { MenuId = command.MenuId }
            : await db.Sys_Menus.SingleAsync(x => x.MenuId == command.MenuId, ct);
        if (command.Kind == "delete")
        {
            db.Sys_Menus.Remove(node);
            return;
        }
        var fields = command.Value!;
        node.MenuName = fields.MenuName;
        node.RoutePath = fields.RoutePath;
        node.MenuKey = fields.MenuKey;
        node.Icon = fields.Icon;
        node.ParentId = fields.ParentId;
        node.OrderNo = fields.OrderNo;
        node.Enable = fields.Enable;
        if (command.Kind == "create") db.Sys_Menus.Add(node);
    }

    private static async Task RequireDefinitionReferencesGone(CP6Context db, int[] ids, bool checkChildren, CancellationToken ct)
    {
        if (await HasRoleOrActionReference(db, ids, ct) || checkChildren &&
            await db.Sys_Menus.AnyAsync(x => x.ParentId.HasValue && ids.Contains(x.ParentId.Value) && !ids.Contains(x.MenuId), ct))
            throw new MenuTreeExceptionV1("MENU_DEFINITION_REFERENCED");
    }

    private static async Task<bool> HasRoleOrActionReference(CP6Context db, int[] ids, CancellationToken ct)
        => await db.Sys_RoleMenus.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.MenuId), ct) ||
           await db.Sys_RoleActions.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.MenuId), ct) ||
           await db.Sys_MenuActions.IgnoreQueryFilters().AnyAsync(x => ids.Contains(x.MenuId), ct);
}
