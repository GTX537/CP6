using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

public sealed partial class MenuTreeAdministrationV1(CP6Context db, ICurrentPermissionContext permissions, IHttpContextAccessor http)
{
    private static byte[] Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
    private static byte[] Key(string operation) => SHA256.HashData(Bytes(operation));
    private static void Operation(string value)
    {
        if (string.IsNullOrEmpty(value)) throw new MenuTreeExceptionV1("MENU_TREE_OPERATION_REQUIRED", 400);
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException e) { throw new MenuTreeExceptionV1("MENU_TREE_OPERATION_INVALID", 400, e); }
    }
    private async Task<T> Transaction<T>(bool write, TenantManagementSessionV1 session,
        Func<PlatformAdminAuthorization.Actor, CancellationToken, Task<T>> action, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql() || db.Database.CurrentTransaction != null || db.ChangeTracker.HasChanges() || System.Transactions.Transaction.Current != null)
            throw new MenuTreeExceptionV1("MENU_TREE_INDEPENDENT_CONTEXT_REQUIRED", 503);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct); var committing = false;
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout='10s'", ct);
            await AuthenticationCompletionGateV1.AcquireAsync(db, ct);
            var current = await session.CurrentActor(ct);
            var actor = await PlatformAdminAuthorization.RequireAsync(db, http.HttpContext?.User, ct, lockActor: true);
            if (current != actor.Id) throw new MenuTreeExceptionV1("REAUTH_REQUIRED", 401);
            var permission = await permissions.GetAsync();
            if (permission.UserId != actor.Id || !permission.ActionKeys.Contains("menu:edit")) throw new MenuTreeExceptionV1("MENU_TREE_FORBIDDEN", 403);
            var until = await db.Sys_Tenants.AsNoTracking().Where(x => x.Id == actor.TenantId).Select(x => x.ExpireDate).SingleAsync(ct);
            var result = await action(actor, ct);
            await session.Complete(ct);
            if (until <= DateTime.Now) throw new MenuTreeExceptionV1("REAUTH_REQUIRED", 401);
            ct.ThrowIfCancellationRequested(); committing = true; await transaction.CommitAsync(ct); return result;
        }
        catch (Exception error)
        {
            if (write && committing) throw new MenuTreeExceptionV1("MENU_TREE_OUTCOME_UNKNOWN", 503, error);
            try { await transaction.RollbackAsync(CancellationToken.None); }
            catch (Exception rollback) { throw new MenuTreeExceptionV1("MENU_TREE_OUTCOME_UNKNOWN", 503, new AggregateException(error, rollback)); }
            throw;
        }
    }
    private async Task<MenuTreeSnapshotV1> Snapshot(CancellationToken ct)
    {
        var revision = await db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_menu_tree_v1.root WHERE singleton").SingleAsync(ct);
        var nodes = await db.Sys_Menus.AsNoTracking().OrderBy(x => x.OrderNo).ThenBy(x => x.MenuId)
            .Select(x => new MenuTreeNodeV1(x.MenuId, x.MenuName, x.RoutePath, x.MenuKey, x.Icon, x.ParentId, x.OrderNo, x.Enable, x.CreateDate)).ToArrayAsync(ct);
        return new(revision, nodes);
    }
    public Task<MenuTreeSnapshotV1> ReadAsync(TenantManagementSessionV1 session, CancellationToken ct = default)
        => Transaction(false, session, (_, token) => Snapshot(token), ct);
    public Task<MenuTreeImpactV1> PreviewAsync(MenuTreeCommandV1 command, TenantManagementSessionV1 session, CancellationToken ct = default)
        => Transaction(false, session, async (_, token) => await Impact(await Snapshot(token), command, token), ct);

    private async Task<MenuTreeImpactV1> Impact(MenuTreeSnapshotV1 current, MenuTreeCommandV1 command, CancellationToken ct)
    {
        if (command.ExpectedRevision <= 0 || command.Positions is not { Length: > 0 } || command.Positions.Any(x => x == null || x.MenuId <= 0 || x.OrderNo < 0)
            || command.Positions.Select(x => x.MenuId).Distinct().Count() != command.Positions.Length)
            throw new MenuTreeExceptionV1("MENU_TREE_INPUT_INVALID", 400);
        if (command.ExpectedRevision != current.Revision) throw new MenuTreeExceptionV1("MENU_TREE_VERSION_CHANGED", 412);
        var before = current.Menus.ToDictionary(x => x.MenuId); var parents = current.Menus.ToDictionary(x => x.MenuId, x => x.ParentId);
        foreach (var position in command.Positions)
        {
            if (!before.ContainsKey(position.MenuId)) throw new MenuTreeExceptionV1("MENU_TREE_NODE_NOT_FOUND", 404);
            if (position.ParentId is { } parent && (!before.ContainsKey(parent) || parent == position.MenuId))
                throw new MenuTreeExceptionV1("MENU_TREE_PARENT_INVALID", 400);
            parents[position.MenuId] = position.ParentId;
        }
        foreach (var node in current.Menus)
        {
            var visited = new HashSet<int>(); int? id = node.MenuId;
            while (id is { } key)
            {
                if (!visited.Add(key) || !parents.TryGetValue(key, out id)) throw new MenuTreeExceptionV1("MENU_TREE_STRUCTURE_INVALID", 400);
            }
        }
        var changed = command.Positions.Where(x => before[x.MenuId].ParentId != x.ParentId || before[x.MenuId].OrderNo != x.OrderNo).Select(x => x.MenuId).Order().ToArray();
        return await References(current, changed, ct);
    }
    private async Task<MenuTreeImpactV1> References(MenuTreeSnapshotV1 current, int[] changed, CancellationToken ct)
    {
        // A shared tree is rendered for every referenced role. Counts conservatively cover
        // the full referenced tree, including ancestor/descendant navigation contexts.
        if (changed.Length == 0) return new(current.Revision, changed, 0, 0, 0, 0);
        var ids = current.Menus.Select(x => x.MenuId).ToArray();
        var menuRefs = db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => ids.Contains(x.MenuId));
        var actionRefs = db.Sys_RoleActions.IgnoreQueryFilters().Where(x => ids.Contains(x.MenuId));
        var roles = await menuRefs.Select(x => new { x.TenantId, x.RoleId }).Union(actionRefs.Select(x => new { x.TenantId, x.RoleId })).ToArrayAsync(ct);
        return new(current.Revision, changed, roles.Select(x => x.TenantId).Distinct().Count(), roles.Length,
            await menuRefs.LongCountAsync(ct), await actionRefs.LongCountAsync(ct));
    }
    private sealed class Saved
    {
        public byte[] Operation { get; set; } = []; public byte[] Input { get; set; } = []; public byte[] Result { get; set; } = [];
        public Guid Actor { get; set; } public Guid ActorTenant { get; set; }
    }
    private Task<Saved?> Original(string operation, CancellationToken ct) => db.Database.SqlQuery<Saved>($"""
        SELECT operation_bytes AS "Operation",input_bytes AS "Input",result_bytes AS "Result",actor_id AS "Actor",actor_tenant AS "ActorTenant"
        FROM cp6_menu_tree_v1.commands WHERE operation_hash={Key(operation)}
        """).SingleOrDefaultAsync(ct);
    private static MenuTreeReceiptV1 Receipt(Saved saved, PlatformAdminAuthorization.Actor actor, string operation, byte[]? input)
    {
        if (saved.Actor != actor.Id || saved.ActorTenant != actor.TenantId) throw new MenuTreeExceptionV1("MENU_TREE_ORIGINAL_FORBIDDEN", 403);
        if (!saved.Operation.AsSpan().SequenceEqual(Bytes(operation)) || input != null && !saved.Input.AsSpan().SequenceEqual(input))
            throw new MenuTreeExceptionV1("MENU_TREE_OPERATION_CONFLICT");
        return JsonSerializer.Deserialize<MenuTreeReceiptV1>(saved.Result)!;
    }
    public Task<MenuTreeReceiptV1?> ReadOriginalAsync(string operation, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        Operation(operation);
        return Transaction<MenuTreeReceiptV1?>(false, session, async (actor, token) =>
        { var saved = await Original(operation, token); return saved == null ? null : Receipt(saved, actor, operation, null); }, ct);
    }
    public Task<MenuTreeReceiptV1> ExecuteAsync(MenuTreeCommandV1 command, TenantManagementSessionV1 session, CancellationToken ct = default)
    {
        Operation(command.OperationRef);
        try { if (command.Reason != null) _ = new UTF8Encoding(false, true).GetByteCount(command.Reason); }
        catch (EncoderFallbackException e) { throw new MenuTreeExceptionV1("MENU_TREE_REASON_INVALID", 400, e); }
        var input = Bytes(command);
        return Transaction(true, session, async (actor, token) =>
        {
            var saved = await Original(command.OperationRef, token);
            if (saved != null) return Receipt(saved, actor, command.OperationRef, input);
            if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 256 || command.Reason.Contains('\0'))
                throw new MenuTreeExceptionV1("MENU_TREE_REASON_REQUIRED", 400);
            var current = await Snapshot(token); var impact = await Impact(current, command, token);
            var ids = impact.ChangedMenuIds;
            var nodes = await db.Sys_Menus.Where(x => ids.Contains(x.MenuId)).ToDictionaryAsync(x => x.MenuId, token);
            foreach (var position in command.Positions.Where(x => nodes.ContainsKey(x.MenuId)))
            { nodes[position.MenuId].ParentId = position.ParentId; nodes[position.MenuId].OrderNo = position.OrderNo; }
            return await CompleteCommand(actor, command.OperationRef, input, command.Reason, "shared-menu-tree-v1", impact, token);
        }, ct);
    }
    private async Task<MenuTreeReceiptV1> CompleteCommand(PlatformAdminAuthorization.Actor actor, string operation, byte[] input,
        string reason, string kind, MenuTreeImpactV1 impact, CancellationToken token)
    {
            db.Sys_SecurityLogs.Add(new Sys_SecurityLog { TenantId = TenantContext.DefaultTenant, UserId = actor.Id, UserName = actor.UserName,
                EventType = (int)SecurityEventType.IamAdministrationChanged, CreatedAt = DateTime.Now, Reason = reason,
                ContextJson = JsonSerializer.Serialize(new { kind, operationHash = Convert.ToHexString(Key(operation)), impact }) });
            await db.SaveChangesAsync(token);
            var applied = await db.Database.SqlQueryRaw<long>("SELECT revision AS \"Value\" FROM cp6_menu_tree_v1.root WHERE singleton").SingleAsync(token);
            var at = await db.Database.SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"").SingleAsync(token);
            var receipt = new MenuTreeReceiptV1(operation, applied, impact, at);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO cp6_menu_tree_v1.commands(operation_hash,operation_bytes,input_bytes,actor_id,actor_tenant,result_bytes)
                VALUES({Key(operation)},{Bytes(operation)},{input},{actor.Id},{actor.TenantId},{Bytes(receipt)})
                """, token);
            return receipt;
    }
}
