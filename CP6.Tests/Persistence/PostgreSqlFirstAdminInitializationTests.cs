using System.Diagnostics;
using System.Security.Cryptography;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Persistence;

[Collection("CurrentPermissionPostgres")]
public sealed class PostgreSqlFirstAdminInitializationTests(CurrentPermissionPostgresFixture postgres)
{
    [CurrentPermissionPostgresFact]
    public async Task DatabaseInit_bootstraps_once_and_preserves_existing_account_and_role_configuration()
    {
        var database = await CreateEmptyDatabase();
        await using var db = CoreMigrationContext(database);
        var connection = db.Database.GetConnectionString()!;

        await RunDatabaseInit(connection, "fresh");
        var original = await ReadAdmin(db);
        Assert.True(original.Enable && original.IsPlatformAdmin && original.RoleId == 1);
        Assert.True(new BCryptPasswordHasher(4).IsHashed(original.Password));
        Assert.Equal(1, await db.Sys_Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await db.Wf_ApprovalBindings.CountAsync(binding => binding.BizType == "PUR_PO" || binding.BizType == "PUR_PR"));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var permissions = await new PermissionAggregator(db).BuildAsync(original.Id);
        Assert.Contains(1, permissions.RoleIds);
        Assert.Contains("dashboard", permissions.MenuKeys);
        Assert.Contains("user", permissions.MenuKeys);
        var menu = await db.Sys_Menus.SingleAsync(row => row.RoutePath == "/dashboard" && row.MenuKey == "dashboard");
        Assert.NotEqual(2, menu.MenuId);
        Assert.True(await db.Sys_Menus.AnyAsync(row => row.MenuId == 2 && row.MenuKey != "dashboard"));
        Assert.True(await db.Sys_Langs.AnyAsync(row => row.LangKey == $"nav.{menu.MenuId}" && row.En == "Dashboard"));
        Assert.False(await db.Sys_Langs.AnyAsync(row => row.LangKey == "nav.2" && row.En == "Dashboard"));

        await RunDatabaseInit(connection, "repeat");
        var repeated = await ReadAdmin(db);
        Assert.True(repeated.Id == original.Id && repeated.Password == original.Password && repeated.IsPlatformAdmin);
        Assert.Equal(1, await db.Sys_Users.IgnoreQueryFilters().CountAsync());

        var lastAdmin = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM public.\"Sys_Users\" WHERE \"Id\"={original.Id}"));
        Assert.Equal("cp6_platform_admin_survivor_v1", lastAdmin.ConstraintName);

        await AddExistingPlatformAdmin(database);
        var role = await db.Sys_Roles.SingleAsync(row => row.RoleId == 1);
        role.RoleName = "Configured role";
        menu.MenuName = "Configured dashboard";
        db.Sys_RoleMenus.RemoveRange(await db.Sys_RoleMenus.Where(row => row.RoleId == 1 && row.MenuId == menu.MenuId).ToListAsync());
        await db.SaveChangesAsync();

        var configuredHash = new BCryptPasswordHasher(4).Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE public."Sys_Users" SET "IsPlatformAdmin"=false,"Password"={configuredHash}
            WHERE "Id"={original.Id}
            """);
        await RunDatabaseInit(connection, "configured");

        var configured = await ReadAdmin(db);
        Assert.True(configured.Id == original.Id && configured.Password == configuredHash);
        Assert.True(configured.Enable);
        Assert.False(configured.IsPlatformAdmin);
        Assert.Equal(2, await db.Sys_Users.IgnoreQueryFilters().CountAsync());
        Assert.Equal(1, await db.Sys_Users.IgnoreQueryFilters().CountAsync(row => row.Enable && row.IsPlatformAdmin));
        Assert.Equal("Configured role", await db.Sys_Roles.Where(row => row.RoleId == 1).Select(row => row.RoleName).SingleAsync());
        Assert.Equal("Configured dashboard", await db.Sys_Menus.Where(row => row.MenuId == menu.MenuId).Select(row => row.MenuName).SingleAsync());
        Assert.False(await db.Sys_RoleMenus.AnyAsync(row => row.RoleId == 1 && row.MenuId == menu.MenuId));

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"Sys_Users\" SET \"Enable\"=false WHERE \"Id\"={original.Id}");
        await RunDatabaseInit(connection, "disabled", "WMS feature approval seed requires an enabled approver");
        var disabled = await ReadAdmin(db);
        Assert.True(disabled.Id == original.Id && disabled.Password == configuredHash);
        Assert.False(disabled.Enable);
        Assert.False(disabled.IsPlatformAdmin);
        Assert.Equal(2, await db.Sys_Users.IgnoreQueryFilters().CountAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task DatabaseInit_resumes_after_migrations_and_default_role_without_restoring_its_navigation()
    {
        var database = await CreateEmptyDatabase();
        await using var db = CoreMigrationContext(database);
        var connection = db.Database.GetConnectionString()!;
        await db.Database.MigrateAsync();
        TenantSeed.EnsureSeeded(db);
        db.Sys_Roles.Add(new Sys_Role { RoleId = 1, RoleName = "管理员", Description = "Existing initializer role" });
        await db.SaveChangesAsync();
        Assert.False(await db.Sys_Users.IgnoreQueryFilters().AnyAsync());

        await RunDatabaseInit(connection, "migrated-role-only");
        var admin = await ReadAdmin(db);
        Assert.True(admin.Enable && admin.IsPlatformAdmin && admin.RoleId == 1);
        Assert.Equal("Existing initializer role", await db.Sys_Roles.Where(row => row.RoleId == 1).Select(row => row.Description).SingleAsync());
        Assert.False(await (from link in db.Sys_RoleMenus
                            join menu in db.Sys_Menus on link.MenuId equals menu.MenuId
                            where link.RoleId == 1 && menu.RoutePath == "/dashboard"
                            select link).AnyAsync());
        Assert.Equal(2, await db.Wf_ApprovalBindings.CountAsync(binding => binding.BizType == "PUR_PO" || binding.BizType == "PUR_PR"));
    }

    [CurrentPermissionPostgresFact]
    public async Task DatabaseInit_atomically_retries_with_an_existing_matching_menu_and_rejects_an_identity_collision()
    {
        var database = await CreateEmptyDatabase();
        await using var db = CoreMigrationContext(database);
        var connection = db.Database.GetConnectionString()!;
        await db.Database.MigrateAsync();
        const int dashboardId = 45678;
        var dashboard = new Sys_Menu { MenuId = dashboardId, MenuName = "Existing dashboard", RoutePath = "/dashboard", MenuKey = "dashboard" };
        var menu = new Sys_Menu { MenuId = 104, ParentId = 100, MenuName = "Preconfigured display", RoutePath = "/other", MenuKey = "other" };
        db.Sys_Menus.AddRange(dashboard, menu);
        await db.SaveChangesAsync();

        await RunDatabaseInit(connection, "menu-conflict", "POSTGRESQL_FIRST_ADMIN_NAVIGATION_CONFLICT");
        Assert.False(await db.Sys_Users.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.Sys_Roles.AnyAsync(row => row.RoleId == 1));
        Assert.Equal("/other", await db.Sys_Menus.Where(row => row.MenuId == 104).Select(row => row.RoutePath).SingleAsync());

        menu.RoutePath = "/user";
        menu.MenuKey = "user";
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION public.bug801_test_abort() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'BUG801_TEST_BOOTSTRAP_ABORT'; END $$;
            CREATE TRIGGER bug801_test_abort BEFORE INSERT ON public."Sys_Users"
              FOR EACH ROW EXECUTE FUNCTION public.bug801_test_abort();
            """);
        await RunDatabaseInit(connection, "insert-failure", "BUG801_TEST_BOOTSTRAP_ABORT");
        Assert.False(await db.Sys_Users.IgnoreQueryFilters().AnyAsync());
        Assert.False(await db.Sys_Roles.AnyAsync(row => row.RoleId == 1));
        Assert.False(await db.Sys_Menus.AnyAsync(row => row.MenuId == 100));
        Assert.False(await db.Sys_RoleMenus.AnyAsync(row => row.RoleId == 1 && row.MenuId == 104));

        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER bug801_test_abort ON public."Sys_Users";
            DROP FUNCTION public.bug801_test_abort();
            """);
        await RunDatabaseInit(connection, "retry-existing-menu");
        var admin = await ReadAdmin(db);
        Assert.True(admin.Enable && admin.IsPlatformAdmin);
        Assert.True(await db.Sys_RoleMenus.AnyAsync(row => row.RoleId == 1 && row.MenuId == 104));
        Assert.True(await db.Sys_RoleMenus.AnyAsync(row => row.RoleId == 1 && row.MenuId == dashboardId));
        Assert.Equal("Preconfigured display", await db.Sys_Menus.Where(row => row.MenuId == 104).Select(row => row.MenuName).SingleAsync());
        Assert.Equal(dashboardId, await db.Sys_Menus.Where(row => row.RoutePath == "/dashboard").Select(row => row.MenuId).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task DatabaseInit_with_users_only_in_another_tenant_refuses_to_create_default_admin()
    {
        var database = await CreateEmptyDatabase();
        await using var db = CoreMigrationContext(database);
        var connection = db.Database.GetConnectionString()!;
        await db.Database.MigrateAsync();
        await AddExistingPlatformAdmin(database);

        await RunDatabaseInit(connection, "different-tenant", "PUR approval seed requires an enabled approver");
        Assert.False(await db.Sys_Users.AnyAsync());
        var existing = await db.Sys_Users.IgnoreQueryFilters().SingleAsync();
        Assert.True(existing.Enable && existing.IsPlatformAdmin && existing.TenantId != TenantContext.DefaultTenant);
    }

    private async Task<string> CreateEmptyDatabase()
    {
        var database = "first_admin_" + Guid.NewGuid().ToString("N");
        await using var owner = await postgres.OpenOwnerAsync("postgres");
        await CurrentPermissionPostgresFixture.Sql(owner, $"CREATE DATABASE {database}");
        return database;
    }

    private static Task<Sys_User> ReadAdmin(CP6Context db) => db.Sys_Users.AsNoTracking().SingleAsync(row => row.UserName == "admin");

    private CP6Context CoreMigrationContext(string database)
    {
        using var fixture = postgres.Context(TenantContext.DefaultTenant, database);
        var provider = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(provider, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), provider,
            fixture.Database.GetConnectionString()!, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        return new CP6Context(options.Options, new TenantContext { CurrentTenantId = TenantContext.DefaultTenant });
    }

    private async Task AddExistingPlatformAdmin(string database)
    {
        var tenant = Guid.NewGuid();
        await using var db = postgres.Context(tenant, database);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenant, TenantCode = "existing", TenantName = "Existing" });
        db.Sys_Users.Add(new Sys_User
        {
            TenantId = tenant, UserName = "existing-platform-admin", Enable = true, IsPlatformAdmin = true,
            Password = new BCryptPasswordHasher(4).Hash(Convert.ToHexString(RandomNumberGenerator.GetBytes(24)))
        });
        await db.SaveChangesAsync();
    }

    private static async Task RunDatabaseInit(string connection, string scenario, string? expectedFailure = null)
    {
        var root = Environment.GetEnvironmentVariable(CurrentPermissionPostgresFactAttribute.RootVariable)!;
        var run = Path.Combine(root, scenario + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(run);
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var dotnet = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_ROOT")!, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        var start = new ProcessStartInfo(dotnet)
        {
            WorkingDirectory = run, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.ArgumentList.Add(typeof(AuthController).Assembly.Location);
        start.ArgumentList.Add("--contentRoot");
        start.ArgumentList.Add(run);
        foreach (var name in start.Environment.Keys.ToArray())
            if (name is not ("SystemRoot" or "WINDIR" or "PATH" or "Path" or "TEMP" or "TMP" or "LOCALAPPDATA" or "APPDATA" or "USERPROFILE"))
                start.Environment.Remove(name);
        foreach (var pair in new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development", ["Database__Provider"] = "PostgreSql",
            ["ConnectionStrings__DefaultConnection"] = connection, ["ConnectionStrings__Redis"] = "",
            ["Startup__Mode"] = "DatabaseInit", ["Startup__SkipDatabaseInitialization"] = "false",
            ["Startup__SkipHostedServices"] = "true", ["JWT__Secret"] = secret,
            ["JWT__Issuer"] = "first-admin-test", ["JWT__Audience"] = "first-admin-test",
            ["CrmOidc__Enabled"] = "false", ["Space__Files__RootPath"] = Path.Combine(run, "space-files")
        }) start.Environment[pair.Key] = pair.Value;

        using var process = Process.Start(start) ?? throw new InvalidOperationException("DatabaseInit process did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        string text;
        try { await process.WaitForExitAsync(timeout.Token); }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            var password = new NpgsqlConnectionStringBuilder(connection).Password!;
            text = (await stdout + await stderr).Replace(connection, "[owned connection]")
                .Replace(password, "[owned credential]").Replace(secret, "[owned signing key]");
            await File.WriteAllTextAsync(Path.Combine(run, "database-init.log"), text);
        }
        Assert.True(expectedFailure is null ? process.ExitCode == 0
            : process.ExitCode != 0 && text.Contains(expectedFailure, StringComparison.Ordinal),
            $"DatabaseInit {scenario} exited {process.ExitCode}. Owned log is at {run}.");
    }
}
