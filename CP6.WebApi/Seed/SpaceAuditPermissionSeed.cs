using System.Data;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.WebApi.Seed;

/// <summary>
/// Seeds the Space audit-read resource and grants it only to each tenant's
/// administrator role. SQL Server instances serialize through a transaction-
/// owned application lock; other providers serialize in-process and use a
/// relational transaction when supported.
/// </summary>
public static class SpaceAuditPermissionSeed
{
    internal const string LockResource =
        "CP6:Seed:SpaceAuditPermission:v1";
    internal const int LockTimeoutMilliseconds = 15_000;
    internal const string AcquireLockCommandText =
        """
        DECLARE @result int;
        EXEC @result = sys.sp_getapplock
            @Resource = @resource,
            @LockMode = N'Exclusive',
            @LockOwner = N'Transaction',
            @LockTimeout = @timeoutMilliseconds,
            @DbPrincipal = N'public';
        SELECT @result;
        """;

    private const int SpaceMenuId = 900;
    private const int AuditMenuId = 906;
    private const int AiAdminMenuId = 907;
    private const int AdministratorRoleId = 1;
    private static readonly SemaphoreSlim NonSqlGate = new(1, 1);
    private static readonly (string Code, string Name, int Sort)[]
        DesignActions =
        [
            ("model:read", "查看设计模型", 10),
            ("model:edit", "编辑设计模型", 20),
            ("source:upload", "关联安全来源", 30),
            ("model:generate-ai", "创建 AI 生成任务", 40),
            ("model:review-ai", "审查 AI 提案", 50),
            ("integration:manage", "管理 WMS 集成", 60),
            ("external:read", "查看外部组织与成员", 70),
            ("external:manage", "管理外部组织与成员", 80),
        ];
    private static readonly (string Code, string Name, int Sort)[]
        AiAdminActions =
        [
            ("read", "查看 AI 策略与用量", 10),
            ("manage", "管理 AI 策略与预算", 20),
        ];

    public static void EnsureSeeded(CP6Context db) =>
        EnsureSeededAsync(db).GetAwaiter().GetResult();

    public static async Task EnsureSeededAsync(
        CP6Context db,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        if (db.Database.IsSqlServer())
        {
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(() =>
                ExecuteSqlServerLockedSeedProtocolAsync(
                    token => db.Database.BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        token),
                    (transaction, token) =>
                        AcquireSqlServerLockAsync(
                            db.Database.GetDbConnection(),
                            transaction.GetDbTransaction(),
                            token),
                    token => SeedAndVerifyAsync(db, token),
                    (transaction, token) =>
                        transaction.CommitAsync(token),
                    ct));
            return;
        }

        await NonSqlGate.WaitAsync(ct);
        try
        {
            if (db.Database.IsRelational())
            {
                await using var transaction = await db.Database
                    .BeginTransactionAsync(
                        IsolationLevel.Serializable,
                        ct);
                await SeedAndVerifyAsync(db, ct);
                await transaction.CommitAsync(ct);
            }
            else
            {
                await SeedAndVerifyAsync(db, ct);
            }
        }
        finally
        {
            NonSqlGate.Release();
        }
    }

    internal static void ConfigureAppLockCommand(DbCommand command)
    {
        command.CommandText = AcquireLockCommandText;
        command.CommandType = CommandType.Text;

        var resource = command.CreateParameter();
        resource.ParameterName = "@resource";
        resource.DbType = DbType.String;
        resource.Size = 255;
        resource.Value = LockResource;
        command.Parameters.Add(resource);

        var timeout = command.CreateParameter();
        timeout.ParameterName = "@timeoutMilliseconds";
        timeout.DbType = DbType.Int32;
        timeout.Value = LockTimeoutMilliseconds;
        command.Parameters.Add(timeout);
    }

    internal static async Task
        ExecuteSqlServerLockedSeedProtocolAsync<TTransaction>(
            Func<CancellationToken, Task<TTransaction>>
                beginTransaction,
            Func<TTransaction, CancellationToken, Task<int>>
                acquireLock,
            Func<CancellationToken, Task> seedAndVerify,
            Func<TTransaction, CancellationToken, Task> commit,
            CancellationToken ct = default)
        where TTransaction : IAsyncDisposable
    {
        await using var transaction =
            await beginTransaction(ct);
        var lockResult = await acquireLock(transaction, ct);
        if (lockResult < 0)
        {
            throw new InvalidOperationException(
                "SPACE_AUDIT_PERMISSION_SEED_LOCK_UNAVAILABLE");
        }

        await seedAndVerify(ct);
        await commit(transaction, ct);
    }

    private static async Task<int> AcquireSqlServerLockAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        ConfigureAppLockCommand(command);
        var result = await command.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                "SPACE_AUDIT_PERMISSION_SEED_LOCK_UNAVAILABLE");
        }

        return Convert.ToInt32(result);
    }

    private static async Task SeedAndVerifyAsync(
        CP6Context db,
        CancellationToken ct)
    {
        var changed = false;

        if (!await db.Sys_Menus.AnyAsync(
                x => x.MenuId == SpaceMenuId,
                ct))
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = SpaceMenuId,
                MenuName = "空间数字底座",
                MenuKey = "space",
                Icon = "OfficeBuilding",
                OrderNo = SpaceMenuId,
                Enable = true,
            });
            changed = true;
        }

        var auditMenu = await db.Sys_Menus.SingleOrDefaultAsync(
            x => x.MenuId == AuditMenuId,
            ct);
        if (auditMenu is null)
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = AuditMenuId,
                MenuName = "事件与审计",
                MenuKey = "space-audit",
                RoutePath = "/space/events",
                Icon = "DocumentChecked",
                ParentId = SpaceMenuId,
                OrderNo = AuditMenuId,
                Enable = true,
            });
            changed = true;
        }
        else if (!string.Equals(
                     auditMenu.MenuKey,
                     "space-audit",
                     StringComparison.Ordinal))
        {
            auditMenu.MenuKey = "space-audit";
            changed = true;
        }

        var aiAdminMenu = await db.Sys_Menus.SingleOrDefaultAsync(
            x => x.MenuId == AiAdminMenuId,
            ct);
        if (aiAdminMenu is null)
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = AiAdminMenuId,
                MenuName = "AI 策略与用量",
                MenuKey = "space-ai-admin",
                RoutePath = "/space/ai-admin",
                Icon = "Cpu",
                ParentId = SpaceMenuId,
                OrderNo = AiAdminMenuId,
                Enable = true,
            });
            changed = true;
        }
        else if (!string.Equals(
                     aiAdminMenu.MenuKey,
                     "space-ai-admin",
                     StringComparison.Ordinal))
        {
            aiAdminMenu.MenuKey = "space-ai-admin";
            changed = true;
        }

        var tenantIds = await db.Sys_Tenants
            .Select(x => x.Id)
            .ToListAsync(ct);
        foreach (var tenantId in tenantIds)
        {
            changed |= await EnsureRoleMenuAsync(
                db,
                tenantId,
                SpaceMenuId,
                ct);
            changed |= await EnsureRoleMenuAsync(
                db,
                tenantId,
                AuditMenuId,
                ct);
            changed |= await EnsureRoleMenuAsync(
                db,
                tenantId,
                AiAdminMenuId,
                ct);

            foreach (var action in DesignActions)
            {
                if (!await db.Sys_MenuActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.MenuId == SpaceMenuId &&
                                x.ActionCode == action.Code,
                            ct))
                {
                    db.Sys_MenuActions.Add(new Sys_MenuAction
                    {
                        TenantId = tenantId,
                        MenuId = SpaceMenuId,
                        ActionCode = action.Code,
                        ActionName = action.Name,
                        Sort = action.Sort,
                    });
                    changed = true;
                }

                if (!await db.Sys_RoleActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.RoleId == AdministratorRoleId &&
                                x.MenuId == SpaceMenuId &&
                                x.ActionCode == action.Code,
                            ct))
                {
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tenantId,
                        RoleId = AdministratorRoleId,
                        MenuId = SpaceMenuId,
                        ActionCode = action.Code,
                    });
                    changed = true;
                }
            }

            if (!await db.Sys_MenuActions
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        x =>
                            x.TenantId == tenantId &&
                            x.MenuId == AuditMenuId &&
                            x.ActionCode == "read",
                        ct))
            {
                db.Sys_MenuActions.Add(new Sys_MenuAction
                {
                    TenantId = tenantId,
                    MenuId = AuditMenuId,
                    ActionCode = "read",
                    ActionName = "查看审计",
                    Sort = 0,
                });
                changed = true;
            }

            if (!await db.Sys_RoleActions
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        x =>
                            x.TenantId == tenantId &&
                            x.RoleId == AdministratorRoleId &&
                            x.MenuId == AuditMenuId &&
                            x.ActionCode == "read",
                        ct))
            {
                db.Sys_RoleActions.Add(new Sys_RoleAction
                {
                    TenantId = tenantId,
                    RoleId = AdministratorRoleId,
                    MenuId = AuditMenuId,
                    ActionCode = "read",
                });
                changed = true;
            }

            foreach (var action in AiAdminActions)
            {
                if (!await db.Sys_MenuActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.MenuId == AiAdminMenuId &&
                                x.ActionCode == action.Code,
                            ct))
                {
                    db.Sys_MenuActions.Add(new Sys_MenuAction
                    {
                        TenantId = tenantId,
                        MenuId = AiAdminMenuId,
                        ActionCode = action.Code,
                        ActionName = action.Name,
                        Sort = action.Sort,
                    });
                    changed = true;
                }

                if (!await db.Sys_RoleActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.RoleId == AdministratorRoleId &&
                                x.MenuId == AiAdminMenuId &&
                                x.ActionCode == action.Code,
                            ct))
                {
                    db.Sys_RoleActions.Add(new Sys_RoleAction
                    {
                        TenantId = tenantId,
                        RoleId = AdministratorRoleId,
                        MenuId = AiAdminMenuId,
                        ActionCode = action.Code,
                    });
                    changed = true;
                }
            }
        }

        if (changed)
            await db.SaveChangesAsync(ct);

        if (!await VerifyInvariantAsync(db, tenantIds, ct))
        {
            throw new InvalidOperationException(
                "SPACE_AUDIT_PERMISSION_SEED_INCOMPLETE");
        }
    }

    private static async Task<bool> EnsureRoleMenuAsync(
        CP6Context db,
        Guid tenantId,
        int menuId,
        CancellationToken ct)
    {
        if (await db.Sys_RoleMenus
            .IgnoreQueryFilters()
            .AnyAsync(
                x =>
                    x.TenantId == tenantId &&
                    x.RoleId == AdministratorRoleId &&
                    x.MenuId == menuId,
                ct))
        {
            return false;
        }

        db.Sys_RoleMenus.Add(new Sys_RoleMenu
        {
            TenantId = tenantId,
            RoleId = AdministratorRoleId,
            MenuId = menuId,
        });
        return true;
    }

    private static async Task<bool> VerifyInvariantAsync(
        CP6Context db,
        IReadOnlyCollection<Guid> tenantIds,
        CancellationToken ct)
    {
        if (!await db.Sys_Menus.AnyAsync(
                x =>
                    x.MenuId == SpaceMenuId &&
                    x.MenuKey == "space",
                ct) ||
            !await db.Sys_Menus.AnyAsync(
                x =>
                    x.MenuId == AuditMenuId &&
                    x.MenuKey == "space-audit",
                ct) ||
            !await db.Sys_Menus.AnyAsync(
                x =>
                    x.MenuId == AiAdminMenuId &&
                    x.MenuKey == "space-ai-admin",
                ct))
        {
            return false;
        }

        foreach (var tenantId in tenantIds)
        {
            var hasSpaceMenu = await db.Sys_RoleMenus
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.RoleId == AdministratorRoleId &&
                        x.MenuId == SpaceMenuId,
                    ct);
            var hasAuditMenu = await db.Sys_RoleMenus
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.RoleId == AdministratorRoleId &&
                        x.MenuId == AuditMenuId,
                    ct);
            var hasAiAdminMenu = await db.Sys_RoleMenus
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.RoleId == AdministratorRoleId &&
                        x.MenuId == AiAdminMenuId,
                    ct);
            var hasAction = await db.Sys_MenuActions
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.MenuId == AuditMenuId &&
                        x.ActionCode == "read",
                    ct);
            var hasGrant = await db.Sys_RoleActions
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.RoleId == AdministratorRoleId &&
                        x.MenuId == AuditMenuId &&
                        x.ActionCode == "read",
                    ct);
            var hasDesignActions = true;
            foreach (var action in DesignActions)
            {
                hasDesignActions &=
                    await db.Sys_MenuActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.MenuId == SpaceMenuId &&
                                x.ActionCode == action.Code,
                            ct) &&
                    await db.Sys_RoleActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.RoleId == AdministratorRoleId &&
                                x.MenuId == SpaceMenuId &&
                                x.ActionCode == action.Code,
                            ct);
            }
            var hasAiAdminActions = true;
            foreach (var action in AiAdminActions)
            {
                hasAiAdminActions &=
                    await db.Sys_MenuActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.MenuId == AiAdminMenuId &&
                                x.ActionCode == action.Code,
                            ct) &&
                    await db.Sys_RoleActions
                        .IgnoreQueryFilters()
                        .AnyAsync(
                            x =>
                                x.TenantId == tenantId &&
                                x.RoleId == AdministratorRoleId &&
                                x.MenuId == AiAdminMenuId &&
                                x.ActionCode == action.Code,
                            ct);
            }
            if (!hasSpaceMenu ||
                !hasAuditMenu ||
                !hasAiAdminMenu ||
                !hasAction ||
                !hasGrant ||
                !hasDesignActions ||
                !hasAiAdminActions)
            {
                return false;
            }
        }

        return true;
    }
}
