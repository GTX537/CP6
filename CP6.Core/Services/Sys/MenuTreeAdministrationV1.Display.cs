using System.Text;
using CP6.Core.Services.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed partial class MenuTreeAdministrationV1
{
    private static void DisplayText(MenuDisplayCommandV1 command)
    {
        // Validate external text before canonical JSON encoding can replace lone surrogates.
        foreach (var value in new[] { command.MenuName, command.Icon, command.Reason })
        {
            if (value == null) continue;
            try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
            catch (EncoderFallbackException e) { throw new MenuTreeExceptionV1("MENU_DISPLAY_TEXT_INVALID", 400, e); }
            if (value.Contains('\0')) throw new MenuTreeExceptionV1("MENU_DISPLAY_TEXT_INVALID", 400);
        }
        if (command.MenuId <= 0 || command.ExpectedRevision <= 0 || string.IsNullOrWhiteSpace(command.MenuName)
            || command.MenuName.Length > 100 || command.Icon?.Length > 100 || string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256)
            throw new MenuTreeExceptionV1("MENU_DISPLAY_INPUT_INVALID", 400);
    }
    private static int[] DisplayChanges(MenuTreeSnapshotV1 current, MenuDisplayCommandV1 command)
    {
        if (command.ExpectedRevision != current.Revision) throw new MenuTreeExceptionV1("MENU_TREE_VERSION_CHANGED", 412);
        var node = current.Menus.SingleOrDefault(x => x.MenuId == command.MenuId) ?? throw new MenuTreeExceptionV1("MENU_TREE_NODE_NOT_FOUND", 404);
        return node.MenuName == command.MenuName && node.Icon == command.Icon && node.Enable == command.Enable ? [] : [node.MenuId];
    }
    public Task<MenuTreeImpactV1> PreviewDisplayAsync(MenuDisplayCommandV1 command, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        DisplayText(command);
        return Transaction(false, session, async (_, token) =>
        { var current = await Snapshot(token); return await References(current, DisplayChanges(current, command), token); }, ct);
    }
    public Task<MenuTreeReceiptV1> ExecuteDisplayAsync(MenuDisplayCommandV1 command, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        Operation(command.OperationRef); DisplayText(command); var input = Bytes(command);
        return Transaction(true, session, async (actor, token) =>
        {
            var saved = await Original(command.OperationRef, token);
            if (saved != null) return Receipt(saved, actor, command.OperationRef, input);
            var current = await Snapshot(token); var changed = DisplayChanges(current, command); var impact = await References(current, changed, token);
            if (changed.Length != 0)
            {
                var node = await db.Sys_Menus.SingleAsync(x => x.MenuId == command.MenuId, token);
                node.MenuName = command.MenuName; node.Icon = command.Icon; node.Enable = command.Enable;
            }
            return await CompleteCommand(actor, command.OperationRef, input, command.Reason, "shared-menu-display-v1", impact, token);
        }, ct);
    }
}
