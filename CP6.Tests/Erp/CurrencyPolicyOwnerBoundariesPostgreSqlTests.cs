using System.Data.Common;
using CP6.Core.Services.Common;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CP6.Tests.Erp;
public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static OperationsOwnerV1 CurrencyAuthority(Harness h, CP6Context? context = null)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var resource in new[] { "currency-policy", "product" })
        { catalog.RegisterScope(resource, resource, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(resource, []); }
        catalog.Seal(); return new(context ?? h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    private static CurrencyPolicyOwnerV1 Currencies(Harness h, CP6Context? context = null) => new(context ?? h.Db, CurrencyAuthority(h, context));
    private static CurrencyPolicyInputV1 CurrencyInput(string code = "JPY") => new()
    { Code = code, Name = "Currency policy " + code, MinorDigits = 0, PriceDigits = 3, Rounding = "HALF_EVEN", Enabled = true };
    private static async Task<CurrencyPolicyCommandV1> NewCurrency(CurrencyPolicyOwnerV1 owner, CurrencyPolicyInputV1? policy = null, Guid? department = null)
        => new() { OperationId = Guid.NewGuid(), PolicyId = Guid.NewGuid(), Action = "CREATE", Policy = policy ?? CurrencyInput(),
            DepartmentId = department, ExpectedAuthorityVersion = (await owner.CreationContextAsync()).AuthorityVersion };
    private static CurrencyPolicyCommandV1 ReviseCurrency(CurrencyPolicyVersionV1 current, CurrencyPolicyInputV1 policy) => new()
    { OperationId = Guid.NewGuid(), PolicyId = current.Reference.Id, Action = "REVISE", ExpectedVersion = current.Reference.Version, Policy = policy };
    private static async Task<CurrencyPolicyVersionV1> MakeCurrency(CurrencyPolicyOwnerV1 owner, CurrencyPolicyInputV1? policy = null)
    { var command = await NewCurrency(owner, policy); await owner.CommandAsync(command); return await owner.ReadAsync(command.PolicyId, null); }
    private sealed class InterruptCurrencyCommit : DbTransactionInterceptor
    {
        internal bool Interrupt;
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { if (Interrupt) { Interrupt = false; throw new InvalidOperationException("Injected pre-commit interruption"); } return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken); }
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_native_history_CAS_no_reused_identity_and_original_close_reject_late_writes()
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h); var owner = Currencies(h);
        var create = await NewCurrency(owner); var receipt = await owner.CommandAsync(create);
        Assert.Equal(receipt, await owner.CommandAsync(create));
        var original = await owner.ReadAsync(create.PolicyId, "1");
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(
            create with { Policy = create.Policy! with { Name = "Different" } }))).Message);
        var disabled = ReviseCurrency(original, original.Policy with { Enabled = false });
        await owner.CommandAsync(disabled);
        Assert.Equal("2", (await owner.ReadAsync(create.PolicyId, null)).Reference.Version);
        var history = await owner.ReadAsync(create.PolicyId, "1");
        Assert.Equal(original.OriginalJson, history.OriginalJson); Assert.False(history.IsCurrent);
        Assert.Equal("CURRENCY_VERSION_CHANGED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(
            disabled with { OperationId = Guid.NewGuid() }))).Message);
        Assert.Equal("CURRENCY_IDENTITY_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(async () =>
            await owner.CommandAsync(await NewCurrency(owner)))).Message);
        var late = await NewCurrency(owner, CurrencyInput("BOX"));
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(late.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync(late.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(late))).Message);
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_currency_policy_v1.heads WHERE tenant_id={h.Tenant}").SingleAsync());
        foreach (var sql in new[] { "UPDATE cp6_currency_policy_v1.versions SET body_bytes=decode('7b7d','hex')",
            "DELETE FROM cp6_currency_policy_v1.versions", "TRUNCATE cp6_currency_policy_v1.versions CASCADE",
            "UPDATE cp6_currency_policy_v1.heads SET revision=1", "DELETE FROM cp6_currency_policy_v1.heads" })
            Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql))).SqlState);
        Assert.Equal(original.OriginalJson, (await owner.ReadAsync(create.PolicyId, "1")).OriginalJson);
    }

    [TenantManagementPostgresTheory]
    [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public async Task CurrencyPolicy_department_creation_is_explicit_current_and_recovery_respects_revocation(int type)
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h); var owner = Currencies(h);
        var hidden = await MakeCurrency(owner); var admin = await MappingAdmin(h);
        var choices = await StandaloneAdmin(h, admin).FindObjectsAsync("currency-policy", "JPY", _ => Task.FromResult(h.User), default);
        Assert.Equal(hidden.Reference.Id, Assert.Single(choices).Id);
        await StandaloneScope(h, admin, type);
        Assert.Empty(await owner.ListAsync(null));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(hidden.Reference.Id, "1"))).Status);
        var missingDepartment = await NewCurrency(owner, CurrencyInput("USD"));
        Assert.Equal("CREATION_DEPARTMENT_FORBIDDEN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(missingDepartment))).Message);
        var explicitCreation = await NewCurrency(owner, CurrencyInput("USD"), admin.Child);
        var created = await owner.CommandAsync(explicitCreation);
        Assert.Equal(created.ObjectId, Assert.Single(await owner.ListAsync(null)).Reference.Id);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_operations_owner_v1.department_mappings SET enabled=false
            WHERE tenant_id={h.Tenant} AND object_kind='currency-policy' AND object_id={created.ObjectId}
            """);
        Assert.Empty(await owner.ListAsync(null));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(explicitCreation.OperationId, false))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_tenants_and_creator_UUID_remain_separate_from_labels_and_legacy_unit_codes()
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h); var owner = Currencies(h);
        h.Db.MasterGenericCodes.Add(new() { GroupCode = "Currency", Code = "JPY", Name = "Legacy code only" });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        Assert.Empty(await owner.ListAsync("JPY"));
        var own = await MakeCurrency(owner);
        await using var foreign = await Seed(); await GrantCurrencyPolicy(foreign);
        var foreignUnit = await MakeCurrency(Currencies(foreign)); Assert.NotEqual(own.Reference.Id, foreignUnit.Reference.Id);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(foreignUnit.Reference.Id, null))).Status);
        await h.Db.Sys_RoleDataScopes.Where(row => row.ResourceKey == "currency-policy").ExecuteUpdateAsync(update => update.SetProperty(row => row.ScopeType, 1));
        await h.Db.Sys_Users.Where(row => row.Id == h.User).ExecuteUpdateAsync(update => update.SetProperty(row => row.UserName, "renamed-" + h.User.ToString("N")));
        Assert.Equal(own.Reference, (await owner.ReadAsync(own.Reference.Id, null)).Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_commit_failure_rolls_back_version_binding_audit_and_original_together()
    {
        var failure = new InterruptCurrencyCommit();
        await using var h = await Seed(failure); await GrantCurrencyPolicy(h); var owner = Currencies(h);
        var admin = await MappingAdmin(h);
        var command = await NewCurrency(owner, department: admin.Child);
        failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
        Assert.Empty(await owner.ListAsync(null));
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_operations_owner_v1.department_mappings
            WHERE tenant_id={h.Tenant} AND object_kind='currency-policy' AND object_id={command.PolicyId}
            """).SingleAsync());
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner='Main.Erp.CurrencyPolicy.v1' AND operation_id={command.OperationId}
            """).SingleAsync());
        Assert.Equal("CLOSED", (await owner.OperationAsync(command.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.CommandAsync(command))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_competing_native_CAS_writers_commit_one_successor_and_one_original()
    {
        await using var h = await Seed(); await GrantCurrencyPolicy(h); var owner = Currencies(h);
        var original = await MakeCurrency(owner);
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = Currencies(h, other);
        async Task<string> Save(CurrencyPolicyOwnerV1 service, string name)
        {
            try { await service.CommandAsync(ReviseCurrency(original, original.Policy with { Name = name })); return "COMMITTED"; }
            catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; }
        }
        var results = await Task.WhenAll(Save(owner, "Writer A"), Save(peer, "Writer B"));
        Assert.Single(results, result => result == "COMMITTED"); Assert.Single(results, result => result == "CURRENCY_VERSION_CHANGED");
        Assert.Equal("2", (await owner.ReadAsync(original.Reference.Id, null)).Reference.Version);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM cp6_main_owner_v1.operations
            WHERE tenant_id={h.Tenant} AND owner='Main.Erp.CurrencyPolicy.v1' AND state='COMMITTED'
            """).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task CurrencyPolicy_forward_migration_preserves_existing_codes_max_menu_ID_and_requires_explicit_grants()
    {
        await using var h = await Seed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var databaseName = "currency_forward_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        {
            await root.OpenAsync();
            // Identifier consists solely of this fixed prefix and fresh lowercase hexadecimal digits.
            await using var command = new NpgsqlCommand("CREATE DATABASE " + databaseName, root); await command.ExecuteNonQueryAsync();
        }
        settings.Database = databaseName;
        var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var policy = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString,
            policy.MigrationsAssembly, policy.HistoryTable, policy.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext());
        await db.GetService<IMigrator>().MigrateAsync("20261026153000_UomProfileOwnerV1");
        db.Sys_Menus.Add(new() { MenuId = int.MaxValue, MenuKey = "historical-max-id", MenuName = "Original menu" });
        db.MasterGenericCodes.Add(new() { Code = "JPY", GroupCode = "Currency", Name = "Original unit code" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        await db.Database.MigrateAsync();
        Assert.Equal("Original menu", await db.Sys_Menus.Where(row => row.MenuId == int.MaxValue).Select(row => row.MenuName).SingleAsync());
        Assert.Equal("Original unit code", await db.MasterGenericCodes.Where(row => row.GroupCode == "Currency" && row.Code == "JPY").Select(row => row.Name).SingleAsync());
        var menuId = await db.Sys_Menus.Where(row => row.MenuKey == "erp-currency-policy").Select(row => row.MenuId).SingleAsync();
        Assert.NotEqual(int.MaxValue, menuId);
        Assert.False(await db.Sys_RoleActions.AnyAsync(row => row.MenuId == menuId));
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM cp6_currency_policy_v1.heads").SingleAsync());
    }

}
