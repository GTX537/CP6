using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CP6.Tests.Space;

public sealed class SpaceAuditPermissionSeedTests
{
    private static readonly string[] AuditLanguageKeys =
    [
        "SPACE_AUTHENTICATION_REQUIRED",
        "SPACE_ACTOR_CONTEXT_REQUIRED",
        "SPACE_TENANT_CONTEXT_REQUIRED",
        "SPACE_EXTERNAL_SUBJECT_DENIED",
        "SPACE_AUDIT_READ_FORBIDDEN",
        "SPACE_CORRELATION_ID_INVALID",
        "SPACE_AUDIT_UNAVAILABLE",
        "SPACE_OPERATION_OUTCOME_UNKNOWN",
        "SPACE_AUDIT_QUERY_RANGE_INVALID",
        "SPACE_AUDIT_QUERY_DISABLED",
    ];

    private static readonly Guid TenantA =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TenantB =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void EnsureSeeded_creates_two_tenant_admin_grants_with_explicit_tenants()
    {
        using var db = NewDb();
        SeedTenants(db);

        SpaceAuditPermissionSeed.EnsureSeeded(db);

        var parent = db.Sys_Menus.Single(x => x.MenuId == 900);
        Assert.Equal("space", parent.MenuKey);
        var audit = db.Sys_Menus.Single(x => x.MenuId == 906);
        Assert.Equal("space-audit", audit.MenuKey);
        Assert.Equal("/space/events", audit.RoutePath);
        Assert.Equal(900, audit.ParentId);
        var aiAdmin = db.Sys_Menus.Single(x => x.MenuId == 907);
        Assert.Equal("space-ai-admin", aiAdmin.MenuKey);
        Assert.Equal("/space/ai-admin", aiAdmin.RoutePath);
        Assert.Equal(900, aiAdmin.ParentId);
        var planning = db.Sys_Menus.Single(x => x.MenuId == 908);
        Assert.Equal("space-planning", planning.MenuKey);
        Assert.Equal("/space/planning", planning.RoutePath);
        Assert.Equal(900, planning.ParentId);

        Assert.All(new[] { TenantA, TenantB }, tenant =>
        {
            Assert.True(db.Sys_RoleMenus.IgnoreQueryFilters().Any(
                x =>
                    x.TenantId == tenant &&
                    x.RoleId == 1 &&
                    x.MenuId == 900));
            Assert.True(db.Sys_RoleMenus.IgnoreQueryFilters().Any(
                x =>
                    x.TenantId == tenant &&
                    x.RoleId == 1 &&
                    x.MenuId == 906));
            Assert.True(db.Sys_RoleMenus.IgnoreQueryFilters().Any(
                x =>
                    x.TenantId == tenant &&
                    x.RoleId == 1 &&
                    x.MenuId == 907));
            Assert.True(db.Sys_RoleMenus.IgnoreQueryFilters().Any(
                x =>
                    x.TenantId == tenant &&
                    x.RoleId == 1 &&
                    x.MenuId == 908));
            Assert.True(db.Sys_MenuActions.IgnoreQueryFilters().Any(
                x =>
                    x.TenantId == tenant &&
                    x.MenuId == 906 &&
                    x.ActionCode == "read"));
            Assert.True(db.Sys_RoleActions.IgnoreQueryFilters().Any(
                x =>
                    x.TenantId == tenant &&
                    x.RoleId == 1 &&
                    x.MenuId == 906 &&
                    x.ActionCode == "read"));
            Assert.All(new[] { "read", "manage" }, action =>
            {
                Assert.True(db.Sys_MenuActions.IgnoreQueryFilters().Any(
                    x =>
                        x.TenantId == tenant &&
                        x.MenuId == 907 &&
                        x.ActionCode == action));
                Assert.True(db.Sys_RoleActions.IgnoreQueryFilters().Any(
                    x =>
                        x.TenantId == tenant &&
                        x.RoleId == 1 &&
                        x.MenuId == 907 &&
                        x.ActionCode == action));
            });
            Assert.All(
                new[]
                {
                    "model:validate",
                    "model:generate-ai",
                    "model:review-ai",
                    "integration:manage",
                    "external:read",
                    "external:manage",
                    "operations:diagnostics:read",
                    "operations:recommendations:read",
                    "operations:recommendations:generate",
                    "operations:dispatch:read",
                    "operations:dispatch:submit",
                    "operations:dispatch:cancel",
                    "operations:dispatch:retry",
                    "operations:dispatch:compensate",
                    "planning:scenario:read",
                    "planning:scenario:create",
                    "planning:dataset:read",
                    "planning:dataset:create",
                    "planning:simulation:read",
                    "planning:simulation:create",
                    "planning:comparison:read",
                    "planning:comparison:create",
                    "planning:decision:read",
                    "planning:decision:create",
                    "planning:exchange:read",
                },
                action =>
                {
                    Assert.True(db.Sys_MenuActions
                        .IgnoreQueryFilters()
                        .Any(x =>
                            x.TenantId == tenant &&
                            x.MenuId == 900 &&
                            x.ActionCode == action));
                    Assert.True(db.Sys_RoleActions
                        .IgnoreQueryFilters()
                        .Any(x =>
                            x.TenantId == tenant &&
                            x.RoleId == 1 &&
                            x.MenuId == 900 &&
                            x.ActionCode == action));
                });
        });

        Assert.DoesNotContain(
            db.Sys_RoleMenus.IgnoreQueryFilters(),
            x => x.TenantId == TenantContext.DefaultTenant);
        Assert.DoesNotContain(
            db.Sys_MenuActions.IgnoreQueryFilters(),
            x => x.TenantId == TenantContext.DefaultTenant);
        Assert.DoesNotContain(
            db.Sys_RoleActions.IgnoreQueryFilters(),
            x => x.TenantId == TenantContext.DefaultTenant);
        Assert.DoesNotContain(
            db.Sys_RoleMenus.IgnoreQueryFilters(),
            x => x.RoleId != 1);
        Assert.DoesNotContain(
            db.Sys_RoleActions.IgnoreQueryFilters(),
            x => x.RoleId != 1);
    }

    [Fact]
    public void EnsureSeeded_is_idempotent_for_every_tenant()
    {
        using var db = NewDb();
        SeedTenants(db);

        SpaceAuditPermissionSeed.EnsureSeeded(db);
        var first = Counts(db);
        SpaceAuditPermissionSeed.EnsureSeeded(db);

        Assert.Equal(first, Counts(db));
        Assert.Equal((4, 8, 2, 2), first);
    }

    [Fact]
    public void EnsureSeeded_repairs_full_admin_menu_set_and_splits_control_tower_from_ai_admin()
    {
        using var db = NewDb();
        SeedTenants(db);
        db.Sys_Menus.AddRange(
            new Sys_Menu
            {
                MenuId = 900,
                MenuName = "空間管理(Space)",
                MenuKey = "space",
                Icon = "Grid",
                Enable = true,
            },
            new Sys_Menu
            {
                MenuId = 901,
                MenuName = "スペースホーム",
                MenuKey = "space-home",
                RoutePath = "/space/home",
                ParentId = 900,
                Enable = true,
            },
            new Sys_Menu
            {
                MenuId = 902,
                MenuName = "サイト管理",
                MenuKey = "space-site",
                RoutePath = "/space/site",
                ParentId = 900,
                Enable = true,
            },
            new Sys_Menu
            {
                MenuId = 903,
                MenuName = "フロア管理",
                MenuKey = "space-floor",
                RoutePath = "/space/floor",
                ParentId = 900,
                Enable = true,
            },
            // Reproduces the post-merge database drift: menu 907 is the
            // control tower row before the AI-admin startup seed runs.
            new Sys_Menu
            {
                MenuId = 907,
                MenuName = "货场控制塔",
                MenuKey = "space-control-tower",
                RoutePath = "/space/control-tower",
                ParentId = 900,
                Enable = true,
            });
        db.SaveChanges();

        SpaceAuditPermissionSeed.EnsureSeeded(db);

        var expectedMenuIds = Enumerable.Range(900, 10).ToArray();
        Assert.Equal(
            expectedMenuIds,
            db.Sys_Menus
                .Where(x => x.MenuId >= 900 && x.MenuId <= 909)
                .OrderBy(x => x.MenuId)
                .Select(x => x.MenuId)
                .ToArray());

        var aiAdmin = db.Sys_Menus.Single(x => x.MenuId == 907);
        Assert.Equal("space-ai-admin", aiAdmin.MenuKey);
        Assert.Equal("/space/ai-admin", aiAdmin.RoutePath);

        var controlTower = db.Sys_Menus.Single(x => x.MenuId == 909);
        Assert.Equal("space-control-tower", controlTower.MenuKey);
        Assert.Equal("/space/control-tower", controlTower.RoutePath);

        Assert.All(new[] { TenantA, TenantB }, tenant =>
        {
            var grantedMenuIds = db.Sys_RoleMenus
                .IgnoreQueryFilters()
                .Where(x => x.TenantId == tenant && x.RoleId == 1)
                .OrderBy(x => x.MenuId)
                .Select(x => x.MenuId)
                .ToArray();
            Assert.Equal(expectedMenuIds, grantedMenuIds);

            Assert.Contains(
                db.Sys_RoleActions.IgnoreQueryFilters(),
                x => x.TenantId == tenant && x.RoleId == 1 &&
                     x.MenuId == 902 && x.ActionCode == "add");
            Assert.Contains(
                db.Sys_RoleActions.IgnoreQueryFilters(),
                x => x.TenantId == tenant && x.RoleId == 1 &&
                     x.MenuId == 903 && x.ActionCode == "edit");
            Assert.Contains(
                db.Sys_RoleActions.IgnoreQueryFilters(),
                x => x.TenantId == tenant && x.RoleId == 1 &&
                     x.MenuId == 904 && x.ActionCode == "generate");
            Assert.Contains(
                db.Sys_RoleActions.IgnoreQueryFilters(),
                x => x.TenantId == tenant && x.RoleId == 1 &&
                     x.MenuId == 905 && x.ActionCode == "publish");
            Assert.Contains(
                db.Sys_RoleActions.IgnoreQueryFilters(),
                x => x.TenantId == tenant && x.RoleId == 1 &&
                     x.MenuId == 909 && x.ActionCode == "view");
        });
    }

    [Fact]
    public void Existing_906_only_converges_menu_key_and_preserves_route()
    {
        using var db = NewDb();
        SeedTenants(db);
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 900,
            MenuName = "Existing Space",
            MenuKey = "space",
            RoutePath = "/existing-parent",
            Enable = false,
        });
        db.Sys_Menus.Add(new Sys_Menu
        {
            MenuId = 906,
            MenuName = "Existing Events",
            MenuKey = "space-events",
            RoutePath = "/custom-existing-events",
            Icon = "ExistingIcon",
            ParentId = 777,
            OrderNo = 42,
            Enable = false,
        });
        db.SaveChanges();

        SpaceAuditPermissionSeed.EnsureSeeded(db);

        var menu = db.Sys_Menus.Single(x => x.MenuId == 906);
        Assert.Equal("space-audit", menu.MenuKey);
        Assert.Equal("/custom-existing-events", menu.RoutePath);
        Assert.Equal("Existing Events", menu.MenuName);
        Assert.Equal("ExistingIcon", menu.Icon);
        Assert.Equal(777, menu.ParentId);
        Assert.Equal(42, menu.OrderNo);
        Assert.False(menu.Enable);
    }

    [Fact]
    public void Existing_regular_role_is_not_granted_audit_read()
    {
        using var db = NewDb();
        SeedTenants(db);
        db.Sys_RoleActions.Add(new Sys_RoleAction
        {
            TenantId = TenantA,
            RoleId = 2,
            MenuId = 123,
            ActionCode = "existing",
        });
        db.SaveChanges();

        SpaceAuditPermissionSeed.EnsureSeeded(db);

        Assert.DoesNotContain(
            db.Sys_RoleActions.IgnoreQueryFilters(),
            x =>
                x.RoleId == 2 &&
                x.MenuId == 906 &&
                x.ActionCode == "read");
        Assert.Contains(
            db.Sys_RoleActions.IgnoreQueryFilters(),
            x =>
                x.TenantId == TenantA &&
                x.RoleId == 2 &&
                x.MenuId == 123 &&
                x.ActionCode == "existing");
    }

    [Fact]
    public void Sql_server_seed_lock_is_exclusive_transaction_owned_and_fail_closed()
    {
        using var command = new SqlCommand();

        SpaceAuditPermissionSeed.ConfigureAppLockCommand(command);

        Assert.Contains(
            "sys.sp_getapplock",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Contains(
            "@LockMode = N'Exclusive'",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Contains(
            "@LockOwner = N'Transaction'",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Contains(
            "SELECT @result",
            command.CommandText,
            StringComparison.Ordinal);
        Assert.Equal(
            SpaceAuditPermissionSeed.LockResource,
            command.Parameters["@resource"].Value);
        Assert.Equal(
            SpaceAuditPermissionSeed.LockTimeoutMilliseconds,
            command.Parameters["@timeoutMilliseconds"].Value);
    }

    [Fact]
    public async Task Sql_server_protocol_orders_transaction_lock_seed_verify_and_commit()
    {
        var steps = new List<string>();

        await SpaceAuditPermissionSeed
            .ExecuteSqlServerLockedSeedProtocolAsync(
                _ =>
                {
                    steps.Add("begin");
                    return Task.FromResult(
                        new RecordingAsyncTransaction(steps));
                },
                (_, _) =>
                {
                    steps.Add("app-lock");
                    return Task.FromResult(0);
                },
                _ =>
                {
                    steps.Add("seed-save-verify");
                    return Task.CompletedTask;
                },
                (_, _) =>
                {
                    steps.Add("commit");
                    return Task.CompletedTask;
                });

        Assert.Equal(
            [
                "begin",
                "app-lock",
                "seed-save-verify",
                "commit",
                "dispose",
            ],
            steps);
    }

    [Fact]
    public async Task Negative_app_lock_result_fails_before_seed_or_commit()
    {
        var steps = new List<string>();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => SpaceAuditPermissionSeed
                .ExecuteSqlServerLockedSeedProtocolAsync(
                    _ =>
                    {
                        steps.Add("begin");
                        return Task.FromResult(
                            new RecordingAsyncTransaction(steps));
                    },
                    (_, _) =>
                    {
                        steps.Add("app-lock");
                        return Task.FromResult(-1);
                    },
                    _ =>
                    {
                        steps.Add("seed-save-verify");
                        return Task.CompletedTask;
                    },
                    (_, _) =>
                    {
                        steps.Add("commit");
                        return Task.CompletedTask;
                    }));

        Assert.Equal(
            "SPACE_AUDIT_PERMISSION_SEED_LOCK_UNAVAILABLE",
            error.Message);
        Assert.Equal(
            ["begin", "app-lock", "dispose"],
            steps);
    }

    [Fact]
    public async Task Sqlite_two_contexts_seed_concurrently_without_duplicates()
    {
        var databaseName = $"space-seed-{Guid.NewGuid():N}";
        var connectionString =
            $"Data Source={databaseName};Mode=Memory;Cache=Shared";
        await using var anchor =
            new SqliteConnection(connectionString);
        await anchor.OpenAsync();
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseSqlite(connectionString)
            .Options;
        await using (var setup = new CP6Context(options))
        {
            await setup.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "Sys_Tenants" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "CreateDate" TEXT NOT NULL,
                    "Creator" TEXT NULL,
                    "Enable" INTEGER NOT NULL,
                    "ExpireDate" TEXT NULL,
                    "Modifier" TEXT NULL,
                    "ModifyDate" TEXT NULL,
                    "Remark" TEXT NULL,
                    "TenantCode" TEXT NOT NULL,
                    "TenantName" TEXT NOT NULL,
                    "TimeZoneId" TEXT NULL,
                    "TwoFactorMode" INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX "UX_Sys_Tenant_Code"
                    ON "Sys_Tenants" ("TenantCode");
                CREATE TABLE "Sys_Menus" (
                    "MenuId" INTEGER NOT NULL PRIMARY KEY,
                    "CreateDate" TEXT NOT NULL,
                    "Enable" INTEGER NOT NULL,
                    "Icon" TEXT NULL,
                    "MenuKey" TEXT NULL,
                    "MenuName" TEXT NOT NULL,
                    "OrderNo" INTEGER NOT NULL,
                    "ParentId" INTEGER NULL,
                    "RoutePath" TEXT NULL
                );
                CREATE UNIQUE INDEX "UX_Sys_Menu_Key"
                    ON "Sys_Menus" ("MenuKey")
                    WHERE "MenuKey" IS NOT NULL;
                CREATE TABLE "Sys_RoleMenus" (
                    "Id" INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    "MenuId" INTEGER NOT NULL,
                    "RoleId" INTEGER NOT NULL,
                    "TenantId" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX "IX_Sys_RoleMenu_Tenant_Role"
                    ON "Sys_RoleMenus"
                    ("TenantId", "RoleId", "MenuId");
                CREATE TABLE "Sys_MenuAction" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "ActionCode" TEXT NOT NULL,
                    "ActionName" TEXT NOT NULL,
                    "CreateDate" TEXT NOT NULL,
                    "Creator" TEXT NULL,
                    "MenuId" INTEGER NOT NULL,
                    "Modifier" TEXT NULL,
                    "ModifyDate" TEXT NULL,
                    "Sort" INTEGER NOT NULL,
                    "TenantId" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX "UX_Sys_MenuAction_MenuAction"
                    ON "Sys_MenuAction"
                    ("TenantId", "MenuId", "ActionCode");
                CREATE TABLE "Sys_RoleAction" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "ActionCode" TEXT NOT NULL,
                    "CreateDate" TEXT NOT NULL,
                    "Creator" TEXT NULL,
                    "MenuId" INTEGER NOT NULL,
                    "Modifier" TEXT NULL,
                    "ModifyDate" TEXT NULL,
                    "RoleId" INTEGER NOT NULL,
                    "TenantId" TEXT NOT NULL
                );
                CREATE UNIQUE INDEX "UX_Sys_RoleAction_RoleMenuAction"
                    ON "Sys_RoleAction"
                    ("TenantId", "RoleId", "MenuId", "ActionCode");
                CREATE TABLE "Sys_FieldAuditLogs" (
                    "Id" TEXT NOT NULL PRIMARY KEY,
                    "ChangedAt" TEXT NOT NULL,
                    "Changes" TEXT NOT NULL,
                    "CreateDate" TEXT NOT NULL,
                    "Creator" TEXT NULL,
                    "EntityKey" TEXT NOT NULL,
                    "EntityName" TEXT NOT NULL,
                    "Modifier" TEXT NULL,
                    "ModifyDate" TEXT NULL,
                    "Operation" INTEGER NOT NULL,
                    "TenantId" TEXT NOT NULL,
                    "UserId" TEXT NULL,
                    "UserName" TEXT NULL
                );
                """);
            SeedTenants(setup);
        }

        await using var first = new CP6Context(options);
        await using var second = new CP6Context(options);

        await Task.WhenAll(
            SpaceAuditPermissionSeed.EnsureSeededAsync(first),
            SpaceAuditPermissionSeed.EnsureSeededAsync(second));

        await using var assertion = new CP6Context(options);
        Assert.Equal((4, 8, 2, 2), Counts(assertion));
        Assert.Equal(
            8,
            await assertion.Sys_RoleMenus
                .IgnoreQueryFilters()
                .Where(x =>
                    x.MenuId == 900 ||
                    x.MenuId == 906 ||
                    x.MenuId == 907 ||
                    x.MenuId == 908)
                .Select(x => new
                {
                    x.TenantId,
                    x.RoleId,
                    x.MenuId,
                })
                .Distinct()
                .CountAsync());
    }

    [Fact]
    public void Audit_error_keys_have_complete_safe_five_language_text()
    {
        var rows = I18nSpaceScreenSeed.Items
            .Where(x => AuditLanguageKeys.Contains(x.LangKey))
            .ToList();

        Assert.Equal(AuditLanguageKeys.Order(), rows.Select(x => x.LangKey).Order());
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
            var all = string.Join(
                "|",
                row.ZhCN,
                row.ZhTW,
                row.En,
                row.Ja,
                row.Ko);
            Assert.DoesNotContain("PayloadJson", all, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LastError", all, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("exception", all, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Ai_admin_screen_has_complete_five_language_text()
    {
        var rows = I18nSpaceAiAdminSeed.Items;

        Assert.Equal(54, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.StartsWith("space.aiAdmin.", row.LangKey, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Planning_scenario_screen_has_complete_five_language_text()
    {
        var rows = I18nSpacePlanningScenarioSeed.Items;

        Assert.Equal(27, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.StartsWith(
                "space.planningScenario.",
                row.LangKey,
                StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Planning_dataset_screen_has_complete_five_language_text()
    {
        var rows = I18nSpacePlanningDatasetSeed.Items;

        Assert.Equal(25, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.StartsWith(
                "space.planningDataset.",
                row.LangKey,
                StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Planning_simulation_screen_has_complete_five_language_text()
    {
        var rows = I18nSpacePlanningSimulationSeed.Items;

        Assert.Equal(41, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.StartsWith(
                "space.planningSimulation.",
                row.LangKey,
                StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Planning_comparison_screen_has_complete_five_language_text()
    {
        var rows = I18nSpacePlanningComparisonSeed.Items;

        Assert.Equal(47, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.StartsWith(
                "space.planningComparison.",
                row.LangKey,
                StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Operations_diagnostics_screen_has_complete_five_language_text()
    {
        var rows = I18nSpaceOperationsDiagnosticsSeed.Items;

        Assert.Equal(35, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.LangKey));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Putaway_recommendation_screen_has_complete_five_language_text()
    {
        var rows = I18nSpacePutawayRecommendationSeed.Items;

        Assert.Equal(42, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.LangKey));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    [Fact]
    public void Dispatch_recommendation_screen_has_complete_five_language_text()
    {
        var rows = I18nSpaceDispatchRecommendationSeed.Items;

        Assert.Equal(119, rows.Length);
        Assert.Equal(rows.Length, rows.Select(row => row.LangKey).Distinct().Count());
        Assert.Contains(rows, row => row.LangKey == "调度效果评估");
        Assert.Contains(rows, row => row.LangKey == "计划几何比较");
        Assert.Contains(rows, row => row.LangKey == "实际路线节省不可用");
        Assert.All(rows, row =>
        {
            Assert.False(string.IsNullOrWhiteSpace(row.LangKey));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhCN));
            Assert.False(string.IsNullOrWhiteSpace(row.ZhTW));
            Assert.False(string.IsNullOrWhiteSpace(row.En));
            Assert.False(string.IsNullOrWhiteSpace(row.Ja));
            Assert.False(string.IsNullOrWhiteSpace(row.Ko));
        });
    }

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(
                w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    private static void SeedTenants(CP6Context db)
    {
        db.Sys_Tenants.AddRange(
            new Sys_Tenant
            {
                Id = TenantA,
                TenantCode = "TA",
                TenantName = "Tenant A",
                Enable = true,
            },
            new Sys_Tenant
            {
                Id = TenantB,
                TenantCode = "TB",
                TenantName = "Tenant B",
                Enable = true,
            });
        db.SaveChanges();
    }

    private static (
        int Menus,
        int RoleMenus,
        int MenuActions,
        int RoleActions) Counts(CP6Context db) =>
        (
            db.Sys_Menus.Count(
                x => x.MenuId == 900 || x.MenuId == 906 ||
                     x.MenuId == 907 || x.MenuId == 908),
            db.Sys_RoleMenus.IgnoreQueryFilters().Count(
                x => x.MenuId == 900 || x.MenuId == 906 ||
                     x.MenuId == 907 || x.MenuId == 908),
            db.Sys_MenuActions.IgnoreQueryFilters().Count(
                x => x.MenuId == 906 && x.ActionCode == "read"),
            db.Sys_RoleActions.IgnoreQueryFilters().Count(
                x =>
                    x.RoleId == 1 &&
                    x.MenuId == 906 &&
                    x.ActionCode == "read")
        );

    private sealed class RecordingAsyncTransaction(
        List<string> steps) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            steps.Add("dispose");
            return ValueTask.CompletedTask;
        }
    }
}
