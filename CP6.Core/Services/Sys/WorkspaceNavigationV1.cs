using System.Globalization;
using CP6.Core.EFDbContext;
using CP6.Core.Services.CrmIdentity;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Client;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed record WorkspaceNavigationSnapshotV1(string MenuVersion, string PolicyVersion,
    IReadOnlyList<ClientMenuDto> Menus, IReadOnlyList<string> ActionKeys);

public sealed class WorkspaceNavigationV1(CP6Context db, ICurrentPermissionContext permissions, OperationsOwnerV1 authority)
{
    public async Task<WorkspaceNavigationSnapshotV1> ReadAsync(IReadOnlySet<string> allowedRoutes, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges() ||
            System.Transactions.Transaction.Current != null)
            throw new CurrentPermissionUnavailableException();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
        await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
        var actor = await authority.CurrentSubjectAsync(ct);
        var current = await permissions.GetAsync();
        if (current.UserId != actor) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);

        var roleKeys = current.RoleIds.Select(IdentityEventContracts.RoleSubject).ToArray();
        var revisions = await db.Database.SqlQuery<Guid>($"""
            SELECT revision AS "Value" FROM cp6_authentication_v1.identity_admission_revisions
            WHERE tenant_id={db.CurrentTenantId} AND object_key=ANY({roleKeys}) ORDER BY object_key COLLATE "C"
            """).ToArrayAsync(ct);
        var tree = await db.Database.SqlQueryRaw<long>(
            """SELECT revision AS "Value" FROM cp6_menu_tree_v1.root WHERE singleton""").SingleAsync(ct);
        var roleMenus = db.Sys_RoleMenus.AsNoTracking().Where(x => current.RoleIds.Contains(x.RoleId)).Select(x => x.MenuId);
        var menus = await db.Sys_Menus.AsNoTracking().Where(x => x.Enable && roleMenus.Contains(x.MenuId))
            .OrderBy(x => x.OrderNo).ThenBy(x => x.MenuId)
            .Select(x => new ClientMenuDto
            {
                Id = x.MenuId, MenuName = x.MenuName, RoutePath = x.RoutePath,
                Icon = x.Icon, ParentId = x.ParentId, OrderNo = x.OrderNo
            }).ToArrayAsync(ct);
        foreach (var menu in menus)
            if (menu.RoutePath is { } path && !allowedRoutes.Contains(path))
                menu.RoutePath = null;

        if (await authority.CurrentSubjectAsync(ct) != actor) throw new OrderPriceCorrectionException("REAUTH_REQUIRED", 401);
        var decision = AuditEvidenceV1.PermissionDecisionVersion(db.CurrentTenantId, current);
        var policyVersion = AuthenticationPolicyServiceV1.Hash(new { decision, revisions, menus = menus.Select(x => x.Id).ToArray() });
        await tx.CommitAsync(ct);
        return new(tree.ToString(CultureInfo.InvariantCulture), policyVersion, menus, current.ActionKeys.Order(StringComparer.Ordinal).ToArray());
    }
}
