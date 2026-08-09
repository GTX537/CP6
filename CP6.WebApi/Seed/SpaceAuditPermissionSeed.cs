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
    private const int HomeMenuId = 901;
    private const int SiteMenuId = 902;
    private const int FloorMenuId = 903;
    private const int CodeRuleMenuId = 904;
    private const int PublishMenuId = 905;
    private const int AuditMenuId = 906;
    private const int AiAdminMenuId = 907;
    private const int PlanningMenuId = 908;
    private const int ControlTowerMenuId = 909;
    private const int AdministratorRoleId = 1;
    private static readonly SemaphoreSlim NonSqlGate = new(1, 1);
    private static readonly int[] ManagedMenuIds =
    [
        SpaceMenuId,
        HomeMenuId,
        SiteMenuId,
        FloorMenuId,
        CodeRuleMenuId,
        PublishMenuId,
        AuditMenuId,
        AiAdminMenuId,
        PlanningMenuId,
        ControlTowerMenuId,
    ];
    private static readonly (
        int MenuId,
        string MenuName,
        string MenuKey,
        string RoutePath,
        string Icon)[] ChildMenus =
    [
        (HomeMenuId, "空间首页", "space-home", "/space/home", "HomeFilled"),
        (SiteMenuId, "场地管理", "space-site", "/space/site", "Location"),
        (FloorMenuId, "楼层管理", "space-floor", "/space/floor", "OfficeBuilding"),
        (CodeRuleMenuId, "编码规则", "space-code-rule", "/space/code-rule", "Tickets"),
        (PublishMenuId, "发布管理", "space-publish", "/space/publish", "Promotion"),
        (AuditMenuId, "事件与审计", "space-audit", "/space/events", "DocumentChecked"),
        (AiAdminMenuId, "AI 策略与用量", "space-ai-admin", "/space/ai-admin", "Cpu"),
        (PlanningMenuId, "规划方案", "space-planning", "/space/planning", "DataAnalysis"),
        (ControlTowerMenuId, "空间控制塔", "space-control-tower", "/space/control-tower", "Monitor"),
    ];
    private static readonly (string Code, string Name, int Sort)[]
        DesignActions =
        [
            ("model:read", "查看设计模型", 10),
            ("model:edit", "编辑设计模型", 20),
            ("model:validate", "校验设计模型", 25),
            ("source:upload", "关联安全来源", 30),
            ("model:generate-ai", "创建 AI 生成任务", 40),
            ("model:review-ai", "审查 AI 提案", 50),
            ("integration:manage", "管理 WMS 集成", 60),
            ("external:read", "查看外部组织与成员", 70),
            ("external:manage", "管理外部组织与成员", 80),
            ("operations:diagnostics:read", "查看运营诊断", 90),
            ("operations:recommendations:read", "查看运营推荐", 100),
            ("operations:recommendations:generate", "生成运营推荐", 110),
            ("operations:dispatch:read", "查看调度审批", 120),
            ("operations:dispatch:submit", "提交调度审批", 130),
            ("operations:dispatch:cancel", "取消调度审批", 140),
            ("operations:dispatch:retry", "重试调度分派", 150),
            ("operations:dispatch:compensate", "补偿调度分派", 160),
            ("planning:scenario:read", "查看规划方案", 170),
            ("planning:scenario:create", "创建规划方案", 180),
            ("planning:dataset:read", "查看脱敏规划数据集", 190),
            ("planning:dataset:create", "导入脱敏规划数据集", 200),
            ("planning:simulation:read", "查看规划仿真", 210),
            ("planning:simulation:create", "运行规划仿真", 220),
            ("planning:comparison:read", "查看规划方案对比", 230),
            ("planning:comparison:create", "创建规划方案对比", 240),
            ("planning:decision:read", "查看规划决策记录", 250),
            ("planning:decision:create", "记录规划决策", 260),
            ("planning:exchange:read", "导出规划交换文件", 270),
        ];
    private static readonly (string Code, string Name, int Sort)[]
        AiAdminActions =
        [
            ("read", "查看 AI 策略与用量", 10),
            ("manage", "管理 AI 策略与预算", 20),
        ];
    private static readonly (int MenuId, string Code, string Name, int Sort)[]
        FeatureActions =
        [
            (SiteMenuId, "add", "新增场地", 10),
            (SiteMenuId, "edit", "编辑场地", 20),
            (SiteMenuId, "delete", "删除场地", 30),
            (FloorMenuId, "add", "新增楼层", 10),
            (FloorMenuId, "edit", "编辑楼层", 20),
            (FloorMenuId, "delete", "删除楼层", 30),
            (CodeRuleMenuId, "add", "新增编码规则", 10),
            (CodeRuleMenuId, "edit", "编辑编码规则", 20),
            (CodeRuleMenuId, "delete", "删除编码规则", 30),
            (CodeRuleMenuId, "generate", "生成编码", 40),
            (PublishMenuId, "publish", "发布版本", 10),
            (PublishMenuId, "deactivate", "停用版本", 20),
            (PublishMenuId, "adopt", "采纳位置版本", 30),
            (ControlTowerMenuId, "view", "查看控制塔", 10),
            (ControlTowerMenuId, "manage", "管理控制塔", 20),
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
        var changed = await EnsureManagedMenusAsync(db, ct);

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

        var planningMenu = await db.Sys_Menus.SingleOrDefaultAsync(
            x => x.MenuId == PlanningMenuId,
            ct);
        if (planningMenu is null)
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = PlanningMenuId,
                MenuName = "规划方案",
                MenuKey = "space-planning",
                RoutePath = "/space/planning",
                Icon = "DataAnalysis",
                ParentId = SpaceMenuId,
                OrderNo = PlanningMenuId,
                Enable = true,
            });
            changed = true;
        }
        else if (!string.Equals(
                     planningMenu.MenuKey,
                     "space-planning",
                     StringComparison.Ordinal))
        {
            planningMenu.MenuKey = "space-planning";
            changed = true;
        }

        var tenantIds = await db.Sys_Tenants
            .Select(x => x.Id)
            .ToListAsync(ct);
        foreach (var tenantId in tenantIds)
        {
            foreach (var menuId in ManagedMenuIds)
            {
                changed |= await EnsureRoleMenuAsync(
                    db,
                    tenantId,
                    menuId,
                    ct);
            }

            foreach (var action in FeatureActions)
            {
                changed |= await EnsureActionGrantAsync(
                    db,
                    tenantId,
                    action.MenuId,
                    action.Code,
                    action.Name,
                    action.Sort,
                    ct);
            }

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

    private static async Task<bool> EnsureManagedMenusAsync(
        CP6Context db,
        CancellationToken ct)
    {
        var changed = false;
        var spaceMenu = await db.Sys_Menus.SingleOrDefaultAsync(
            x => x.MenuId == SpaceMenuId,
            ct);
        if (spaceMenu is null)
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = SpaceMenuId,
                MenuName = "空间管理",
                MenuKey = "space",
                Icon = "OfficeBuilding",
                OrderNo = SpaceMenuId,
                Enable = true,
            });
            changed = true;
        }
        else if (!string.Equals(
                     spaceMenu.MenuKey,
                     "space",
                     StringComparison.Ordinal))
        {
            spaceMenu.MenuKey = "space";
            changed = true;
        }

        // 907 曾被控制塔 SQL 种子复用。先恢复 AI 菜单并落库，释放
        // space-control-tower 唯一键，再创建控制塔的永久编号 909。
        foreach (var menu in ChildMenus.Where(
                     x => x.MenuId != ControlTowerMenuId))
        {
            changed |= await EnsureCanonicalMenuAsync(
                db,
                menu,
                canonicalizeExisting: menu.MenuId != AuditMenuId,
                ct);
        }

        var anyChanged = changed;
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }

        var controlTower = ChildMenus.Single(
            x => x.MenuId == ControlTowerMenuId);
        changed = await EnsureCanonicalMenuAsync(
            db,
            controlTower,
            canonicalizeExisting: true,
            ct);
        return anyChanged || changed;
    }

    private static async Task<bool> EnsureCanonicalMenuAsync(
        CP6Context db,
        (int MenuId,
            string MenuName,
            string MenuKey,
            string RoutePath,
            string Icon) definition,
        bool canonicalizeExisting,
        CancellationToken ct)
    {
        var menu = await db.Sys_Menus.SingleOrDefaultAsync(
            x => x.MenuId == definition.MenuId,
            ct);
        if (menu is null)
        {
            db.Sys_Menus.Add(new Sys_Menu
            {
                MenuId = definition.MenuId,
                MenuName = definition.MenuName,
                MenuKey = definition.MenuKey,
                RoutePath = definition.RoutePath,
                Icon = definition.Icon,
                ParentId = SpaceMenuId,
                OrderNo = definition.MenuId,
                Enable = true,
            });
            return true;
        }

        var changed = false;
        if (!string.Equals(
                menu.MenuKey,
                definition.MenuKey,
                StringComparison.Ordinal))
        {
            menu.MenuKey = definition.MenuKey;
            changed = true;
        }

        if (!canonicalizeExisting)
        {
            return changed;
        }

        if (!string.Equals(menu.MenuName, definition.MenuName, StringComparison.Ordinal))
        {
            menu.MenuName = definition.MenuName;
            changed = true;
        }
        if (!string.Equals(menu.RoutePath, definition.RoutePath, StringComparison.Ordinal))
        {
            menu.RoutePath = definition.RoutePath;
            changed = true;
        }
        if (!string.Equals(menu.Icon, definition.Icon, StringComparison.Ordinal))
        {
            menu.Icon = definition.Icon;
            changed = true;
        }
        if (menu.ParentId != SpaceMenuId)
        {
            menu.ParentId = SpaceMenuId;
            changed = true;
        }
        if (menu.OrderNo != definition.MenuId)
        {
            menu.OrderNo = definition.MenuId;
            changed = true;
        }
        if (!menu.Enable)
        {
            menu.Enable = true;
            changed = true;
        }

        return changed;
    }

    private static async Task<bool> EnsureActionGrantAsync(
        CP6Context db,
        Guid tenantId,
        int menuId,
        string actionCode,
        string actionName,
        int sort,
        CancellationToken ct)
    {
        var changed = false;
        var menuAction = await db.Sys_MenuActions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x =>
                    x.TenantId == tenantId &&
                    x.MenuId == menuId &&
                    x.ActionCode == actionCode,
                ct);
        if (menuAction is null)
        {
            db.Sys_MenuActions.Add(new Sys_MenuAction
            {
                TenantId = tenantId,
                MenuId = menuId,
                ActionCode = actionCode,
                ActionName = actionName,
                Sort = sort,
            });
            changed = true;
        }
        else
        {
            if (!string.Equals(
                    menuAction.ActionName,
                    actionName,
                    StringComparison.Ordinal))
            {
                menuAction.ActionName = actionName;
                changed = true;
            }
            if (menuAction.Sort != sort)
            {
                menuAction.Sort = sort;
                changed = true;
            }
        }

        if (!await db.Sys_RoleActions
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.RoleId == AdministratorRoleId &&
                        x.MenuId == menuId &&
                        x.ActionCode == actionCode,
                    ct))
        {
            db.Sys_RoleActions.Add(new Sys_RoleAction
            {
                TenantId = tenantId,
                RoleId = AdministratorRoleId,
                MenuId = menuId,
                ActionCode = actionCode,
            });
            changed = true;
        }

        return changed;
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
        foreach (var definition in ChildMenus)
        {
            var hasCanonicalMenu = await db.Sys_Menus.AnyAsync(
                x =>
                    x.MenuId == definition.MenuId &&
                    x.MenuKey == definition.MenuKey &&
                    (definition.MenuId == AuditMenuId ||
                     (x.RoutePath == definition.RoutePath &&
                      x.ParentId == SpaceMenuId &&
                      x.Enable)),
                ct);
            if (!hasCanonicalMenu)
            {
                return false;
            }
        }

        foreach (var tenantId in tenantIds)
        {
            foreach (var menuId in ManagedMenuIds)
            {
                if (!await db.Sys_RoleMenus
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
            }

            foreach (var action in FeatureActions)
            {
                var hasAction = await db.Sys_MenuActions
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        x =>
                            x.TenantId == tenantId &&
                            x.MenuId == action.MenuId &&
                            x.ActionCode == action.Code,
                        ct);
                var hasGrant = await db.Sys_RoleActions
                    .IgnoreQueryFilters()
                    .AnyAsync(
                        x =>
                            x.TenantId == tenantId &&
                            x.RoleId == AdministratorRoleId &&
                            x.MenuId == action.MenuId &&
                            x.ActionCode == action.Code,
                        ct);
                if (!hasAction || !hasGrant)
                {
                    return false;
                }
            }
        }

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
                ct) ||
            !await db.Sys_Menus.AnyAsync(
                x =>
                    x.MenuId == PlanningMenuId &&
                    x.MenuKey == "space-planning",
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
            var hasPlanningMenu = await db.Sys_RoleMenus
                .IgnoreQueryFilters()
                .AnyAsync(
                    x =>
                        x.TenantId == tenantId &&
                        x.RoleId == AdministratorRoleId &&
                        x.MenuId == PlanningMenuId,
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
                !hasPlanningMenu ||
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
