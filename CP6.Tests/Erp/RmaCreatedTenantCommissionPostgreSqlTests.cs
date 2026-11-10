using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using CP6.Core.Persistence.Tenancy;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DomainModels.Wms;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

internal sealed class RmaCreatedTenantCommissionFactAttribute : FactAttribute
{
    public RmaCreatedTenantCommissionFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CP6_RMA_CREATED_TENANT_COMMISSION") is null)
            Skip = "Run this case separately with its owned PostgreSQL fixture and cluster roles.";
    }
}

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [RmaCreatedTenantCommissionFact]
    public async Task Created_tenant_uses_original_receipt_to_commission_and_only_then_write_RMA()
    {
        if (!CurrentPermissionPostgresFactAttribute.Selected)
            throw new InvalidOperationException("This run requires the owned PostgreSQL fixture root and binaries.");
        var actor = Guid.NewGuid();
        var creatorTenant = TenantContext.DefaultTenant;
        var operationRef = "创建/原键\0\"\\" + Guid.NewGuid().ToString("N");
        var database = "created_rma_" + Guid.NewGuid().ToString("N");
        var login = "rma_runtime_" + Guid.NewGuid().ToString("N");
        var wrongOwner = "rma_other_" + Guid.NewGuid().ToString("N");
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var creatorPassword = "Creator!" + Guid.NewGuid().ToString("N");
        await using var parent = server.Context(new TenantContext { CurrentTenantId = creatorTenant });
        var settings = new NpgsqlConnectionStringBuilder(parent.Database.GetConnectionString()!)
        { Database = "postgres", Pooling = false, Enlist = false, CommandTimeout = 90 };
        await using (var cluster = new NpgsqlConnection(settings.ConnectionString))
        {
            await cluster.OpenAsync();
            await CurrentPermissionPostgresFixture.Sql(cluster, $"CREATE DATABASE {database}");
        }
        settings.Database = database;
        var connection = settings.ConnectionString;
        var oid = (uint)(await ReadAsync("SELECT oid FROM pg_catalog.pg_database WHERE datname=current_database()"))!;
        var system = (string)(await ReadAsync("SELECT system_identifier::text FROM pg_catalog.pg_control_system()"))!;
        var target = new RmaTenantTargetV1("127.0.0.1", settings.Port, database, oid, system);
        var plan = new RmaCreatedTenantCoreCommissionRequestV2(target, Guid.NewGuid(), Guid.NewGuid(),
            [new(operationRef, creatorTenant, actor, login)]);

        var prepared = await ControlAsync(c => RmaTenantStorageV1.PrepareCoreFromCreationAsync(c, plan, default));
        Assert.Equal(prepared, await ControlAsync(c => RmaTenantStorageV1.PrepareCoreFromCreationAsync(c, plan, default)));
        var (creatorCode, menuId) = await SeedCreatedCommissionAuthorityAsync();
        var pending = await CommissionStateAsync();
        Assert.Equal("core-tenant-creation-rejected", (await Assert.ThrowsAsync<TenantProvisioningException>(() => CommissionAsync())).Code);
        Assert.Equal("core-preparation-conflict", (await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            ControlAsync(c => RmaTenantStorageV1.CommissionCoreFromCreationAsync(c,
                plan with { Creations = [plan.Creations[0] with { CreatorActorId = Guid.NewGuid() }] }, default)))).Code);
        Assert.Equal(pending, await CommissionStateAsync());

        const string creation = "api/platform/tenant/creation-v1";
        const string userName = "first-rma-admin";
        var tenantCode = "new_rma_" + Guid.NewGuid().ToString("N");
        TenantCreationResultV1 first;
        await using (var creatorHost = await AuthenticationApiProcess.Start(connection))
        {
            var cookies = await Login(creatorHost, "rma-creator", creatorPassword, creatorCode);
            using var template = await creatorHost.Send(OperationsRequest(cookies, HttpMethod.Get, creation + "/template"));
            var selected = await NumberHttpData<TenantCreationTemplateV1>(template);
            var command = new TenantCreationCommandV1(operationRef, selected.Version, tenantCode, "新 RMA 租户", null, null, userName);
            using var created = await creatorHost.Send(OperationsRequest(cookies, HttpMethod.Post, creation + "/commands", command));
            first = await NumberHttpData<TenantCreationResultV1>(created);
            using var original = await creatorHost.Send(OperationsRequest(cookies, HttpMethod.Post,
                creation + "/commands/read", new { command.OperationRef }));
            SameCreation(first.Receipt, await NumberHttpData<TenantCreationResultV1>(original));
            using var same = await creatorHost.Send(OperationsRequest(cookies, HttpMethod.Post, creation + "/commands", command));
            SameCreation(first.Receipt, await NumberHttpData<TenantCreationResultV1>(same));
        }

        Assert.NotNull(first.TempPassword);
        var tenant = first.Receipt.TenantId;
        var schema = "t_" + tenant.ToString("N");
        var resolved = new RmaCoreCommissionRequestV1(target, plan.InstallationId, plan.OperationId, [new(tenant, login)]);
        var outboundId = await SeedCreatedRmaSourceAsync(tenant, first.Receipt.AdminUserId, first.Receipt.Template.MenuIds, menuId, userName);
        Assert.Equal("core-preparation-conflict", (await Assert.ThrowsAsync<TenantProvisioningException>(() =>
            ControlAsync(c => RmaTenantStorageV1.CommissionCoreAsync(c, resolved, default)))).Code);
        await SqlAsync($"CREATE ROLE {login} LOGIN PASSWORD '{password}'");

        var config = new Dictionary<string, string>
        {
            ["RmaReadTenancy__Enabled"] = "true", ["RmaReadTenancy__DraftEnabled"] = "true",
            ["RmaReadTenancy__InstallationId"] = plan.InstallationId.ToString("D"),
            ["RmaReadTenancy__Revision"] = "created-rma-fixture", ["RmaReadTenancy__SslMode"] = "Disable",
            ["RmaReadTenancy__Tenants__0__TenantId"] = tenant.ToString("D"), ["RmaReadTenancy__Tenants__0__Login"] = login,
            ["RmaReadTenancy__Tenants__0__Password"] = password, ["RmaReadTenancy__Tenants__0__CredentialVersion"] = "fixture-1"
        };
        await using var host = await AuthenticationApiProcess.Start(connection, config);
        var temporary = await Login(host, userName, first.TempPassword, tenantCode);
        var changedPassword = "First!" + Guid.NewGuid().ToString("N");
        using (var changed = await host.Send(OperationsRequest(temporary, HttpMethod.Post, "api/auth/change-password",
            new { CurrentPassword = first.TempPassword, NewPassword = changedPassword })))
            Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));
        var session = await Login(host, userName, changedPassword, tenantCode);
        const string rma = "api/wms/rma/native-applications-v1";
        using var sourceResponse = await host.Send(OperationsRequest(session, HttpMethod.Get, $"{rma}/sources/{outboundId:D}"));
        var source = await NumberHttpData<RmaApplicationSourceV1>(sourceResponse);
        var draft = new RmaApplicationCreateV1(Guid.NewGuid(), Guid.NewGuid(), source.Id, source.Version,
            "commissioned tenant", "RETURN_ONLY", [new(source.Lines.Single().Id, "1.12345678")], null);
        using (var unavailable = await SendDraft())
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
            Assert.Contains("RMA_TENANT_BINDING_UNAVAILABLE", await unavailable.Content.ReadAsStringAsync());
        }
        Assert.Equal(pending, await CommissionStateAsync());

        await SqlAsync($"CREATE ROLE {wrongOwner} NOLOGIN; CREATE SCHEMA {schema} AUTHORIZATION {wrongOwner}");
        var collision = await Assert.ThrowsAsync<PostgresException>(() => CommissionAsync());
        Assert.Equal("42P06", collision.SqlState);
        Assert.Equal(wrongOwner, await ReadAsync($"SELECT pg_get_userbyid(nspowner) FROM pg_namespace WHERE nspname='{schema}'"));
        Assert.Equal(pending, await CommissionStateAsync());
        await SqlAsync($"DROP SCHEMA {schema}");

        await InterruptCommissionAfterSchemaAsync();
        Assert.Equal(true, await ReadAsync($"SELECT pg_catalog.to_regnamespace('{schema}') IS NULL"));
        Assert.Equal(pending, await CommissionStateAsync());

        _ = await CommissionAsync(); // Discard the first successful result and recover over a fresh connection.
        var durable = Assert.IsType<string>(await ReadAsync("SELECT commission_receipt FROM cp6_rma_core_commission_v1.installation"));
        Assert.Equal(durable, await CommissionAsync());
        Assert.Equal(durable, await ControlAsync(c => RmaTenantStorageV1.CommissionCoreAsync(c, resolved, default)));
        Assert.Equal(JsonSerializer.Serialize(resolved), await ReadAsync("SELECT request_json FROM cp6_rma_core_commission_v1.installation"));
        Assert.Equal(prepared, await ControlAsync(c => RmaTenantStorageV1.PrepareCoreFromCreationAsync(c, plan, default)));
        Assert.Equal(tenant, await ReadAsync("SELECT tenant_id FROM cp6_rma_tenant_v1.tenant_migrations WHERE state='Ready'"));
        var ordinary = new NpgsqlConnectionStringBuilder(connection) { Username = login, Password = password, SearchPath = "pg_catalog" };
        await using (var runtime = new NpgsqlConnection(ordinary.ConnectionString))
        {
            await runtime.OpenAsync();
            Assert.Equal(tenant, await CurrentPermissionPostgresFixture.Scalar(runtime, "SELECT cp6_tenancy_v1.current_tenant_id()"));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => CurrentPermissionPostgresFixture.Scalar(runtime,
                "SELECT result_bytes FROM cp6_tenant_creation_v1.commands"));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
        }

        await ControlAsync(c => RmaTenantStorageV1.InstallCoreOriginalJournalAsync(c, resolved with { OperationId = Guid.NewGuid() }, default));
        await ControlAsync(c => RmaTenantStorageV1.InstallCoreDraftWriterAsync(c, resolved with { OperationId = Guid.NewGuid() }, default));
        using var applied = await SendDraft();
        Assert.True(applied.StatusCode == HttpStatusCode.Created, await OperationsFailure(applied));
        using var replay = await SendDraft();
        Assert.True(replay.StatusCode == HttpStatusCode.Created, await OperationsFailure(replay));
        Assert.Equal(await applied.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1L, await ReadAsync($"SELECT count(*) FROM {schema}.heads WHERE id='{draft.ApplicationId:D}' AND actor_id='{first.Receipt.AdminUserId:D}'"));
        Assert.Equal(1L, await ReadAsync($"SELECT count(*) FROM cp6_main_owner_v1.operations WHERE owner='Main.Wms.RmaApplication.v1' AND operation_id='{draft.OperationId:D}'"));

        Task<string> CommissionAsync() => ControlAsync(c => RmaTenantStorageV1.CommissionCoreFromCreationAsync(c, plan, default));
        Task<HttpResponseMessage> SendDraft() => host.Send(OperationsRequest(session, HttpMethod.Post, rma, draft));

        async Task InterruptCommissionAfterSchemaAsync()
        {
            const string application = "w433-provision-interruption";
            await using var blocker = new NpgsqlConnection(connection);
            await blocker.OpenAsync();
            await CurrentPermissionPostgresFixture.Sql(blocker, $"""
                CREATE FUNCTION public.w433_fixture_schema_barrier() RETURNS event_trigger
                LANGUAGE plpgsql SET search_path=pg_catalog AS $fixture$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_event_trigger_ddl_commands()
                        WHERE classid='pg_namespace'::regclass AND objid=to_regnamespace('{schema}')) THEN
                        RAISE LOG 'W433 fixture reached tenant schema DDL in backend %', pg_backend_pid();
                        PERFORM pg_advisory_xact_lock(433020,1);
                    END IF;
                END $fixture$;
                CREATE EVENT TRIGGER w433_fixture_schema_barrier ON ddl_command_end WHEN TAG IN ('CREATE SCHEMA')
                    EXECUTE FUNCTION public.w433_fixture_schema_barrier();
                SELECT pg_catalog.pg_advisory_lock(433020,1);
                """);
            await using var interrupted = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection)
            { ApplicationName = application }.ConnectionString);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                var attempt = RmaTenantStorageV1.CommissionCoreFromCreationAsync(interrupted, plan, timeout.Token);
                int? backend = null;
                while (backend is null)
                {
                    Assert.False(attempt.IsCompleted);
                    backend = await CurrentPermissionPostgresFixture.Scalar(blocker, $"""
                        SELECT pid FROM pg_catalog.pg_stat_activity
                        WHERE datname=current_database() AND application_name='{application}'
                          AND wait_event_type='Lock' AND pg_backend_pid()=ANY(pg_blocking_pids(pid))
                        """, timeout.Token) as int?;
                    if (backend is null) await Task.Delay(25, timeout.Token);
                }
                Assert.Equal(true, await CurrentPermissionPostgresFixture.Scalar(blocker,
                    $"SELECT pg_catalog.pg_terminate_backend({backend.Value},5000)", timeout.Token));
                await Assert.ThrowsAnyAsync<NpgsqlException>(() => attempt);
            }
            finally
            {
                await CurrentPermissionPostgresFixture.Sql(blocker, """
                    SELECT pg_catalog.pg_advisory_unlock(433020,1);
                    DROP EVENT TRIGGER w433_fixture_schema_barrier;
                    DROP FUNCTION public.w433_fixture_schema_barrier();
                    """);
            }
        }

        async Task<T> ControlAsync<T>(Func<NpgsqlConnection, Task<T>> call)
        {
            await using var control = new NpgsqlConnection(connection);
            return await call(control);
        }
        async Task<object?> ReadAsync(string sql) => await ControlAsync(async control =>
        {
            await control.OpenAsync();
            return await CurrentPermissionPostgresFixture.Scalar(control, sql);
        });
        async Task SqlAsync(string sql) => await ControlAsync(async control =>
        {
            await control.OpenAsync();
            return await CurrentPermissionPostgresFixture.Sql(control, sql);
        });
        Task<object?> CommissionStateAsync() => ReadAsync("""
            SELECT jsonb_build_object('request',request_json,'plan',creation_request_json,'receipt',commission_receipt,
                'layout',to_regnamespace('cp6_rma_tenant_v1') IS NOT NULL,
                'directory',to_regnamespace('cp6_tenancy_v1') IS NOT NULL,
                'legacyHeads',(SELECT count(*) FROM cp6_rma_application_v1.heads),
                'legacyJournal',(SELECT count(*) FROM cp6_main_owner_v1.operations WHERE owner='Main.Wms.RmaApplication.v1'),
                'guards',(SELECT count(*) FROM pg_trigger WHERE tgname='rma_core_legacy_disabled'))::text
            FROM cp6_rma_core_commission_v1.installation;
            """);

        CP6Context ContextFor(Guid id)
        {
            var provider = new DatabaseOptions(DatabaseProvider.PostgreSql);
            var profile = DatabaseMigrationProfile.For(provider, DatabaseContextKind.Core);
            var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), provider,
                connection, profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
            return new CP6Context(options.Options, new TenantContext { CurrentTenantId = id });
        }

        async Task<(string TenantCode, int MenuId)> SeedCreatedCommissionAuthorityAsync()
        {
            await using var db = ContextFor(creatorTenant);
            await db.Database.MigrateAsync();
            var tenantRow = await db.Sys_Tenants.SingleOrDefaultAsync(x => x.Id == creatorTenant);
            if (tenantRow is null)
                db.Sys_Tenants.Add(tenantRow = new() { Id = creatorTenant, TenantCode = "creation-core", TenantName = "Template" });
            tenantRow.Enable = true;
            // The menu writer advances the catalog only for tenants already visible to its trigger.
            await db.SaveChangesAsync();
            var role = await db.Sys_Roles.SingleOrDefaultAsync(x => x.RoleId == 1);
            if (role is null) db.Sys_Roles.Add(new() { RoleId = 1, RoleName = "Template", Enable = true });
            else role.Enable = true;
            var menu = await db.Sys_Menus.SingleOrDefaultAsync(x => x.MenuKey == "wms-rma");
            if (menu is null) db.Sys_Menus.Add(menu = new() { MenuId = 300810000, MenuKey = "wms-rma", MenuName = "RMA", Enable = true });
            else menu.Enable = true;
            db.Sys_Users.Add(new() { Id = actor, UserName = "rma-creator", RoleId = 1, Enable = true,
                Password = new BCryptPasswordHasher(4).Hash(creatorPassword) });
            await db.SaveChangesAsync();
            if (!await db.Sys_RoleMenus.AnyAsync(x => x.RoleId == 1 && x.MenuId == menu.MenuId))
                db.Sys_RoleMenus.Add(new() { RoleId = 1, MenuId = menu.MenuId });
            await db.SaveChangesAsync();
            await db.Sys_Users.Where(x => x.Id == actor).ExecuteUpdateAsync(update => update.SetProperty(x => x.IsPlatformAdmin, true));
            return (tenantRow.TenantCode, menu.MenuId);
        }

        async Task<Guid> SeedCreatedRmaSourceAsync(Guid id, Guid admin, int[] templateMenus, int menuId, string name)
        {
            await using var db = ContextFor(id);
            Assert.True(await db.Sys_Tenants.Where(x => x.Id == id).Select(x => x.Enable).SingleAsync());
            Assert.Equal(admin, (await db.Sys_Users.IgnoreQueryFilters().SingleAsync(x => x.TenantId == id)).Id);
            Assert.Equal(1, (await db.Sys_Roles.IgnoreQueryFilters().SingleAsync(x => x.TenantId == id)).RoleId);
            Assert.Equal(templateMenus, await db.Sys_RoleMenus.IgnoreQueryFilters().Where(x => x.TenantId == id)
                .OrderBy(x => x.MenuId).Select(x => x.MenuId).ToArrayAsync());
            foreach (var action in new[] { "query", "add" })
            {
                if (!await db.Sys_MenuActions.AnyAsync(x => x.MenuId == menuId && x.ActionCode == action))
                    db.Sys_MenuActions.Add(new() { MenuId = menuId, ActionCode = action, ActionName = action });
                db.Sys_RoleActions.Add(new() { RoleId = 1, MenuId = menuId, ActionCode = action });
            }
            db.Sys_RoleDataScopes.Add(new() { RoleId = 1, ResourceKey = "wms-rma", ScopeType = 5 });
            db.WmsRoleScopes.Add(new() { RoleId = 1, WarehouseCd = "W1" });
            var outbound = new OutboundOrder { Id = Guid.NewGuid(), OutboundNo = "W433-OUT", OutboundType = OutboundType.Shipping,
                Status = OutboundOrderStatus.Completed, CustomerCd = "C1", WarehouseCd = "W1", Creator = name };
            db.OutboundOrders.Add(outbound);
            db.OutboundOrderDetails.Add(new() { Id = Guid.NewGuid(), OutboundNo = outbound.OutboundNo, LineNo = 1,
                ProductCd = "P1", RequiredQty = 3m, AllocatedQty = 3m, ShippedQty = 3m, ShipTxnNo = "W433-SHIP",
                UnitCd = "EA", LocationCd = "L1", Creator = name });
            await db.SaveChangesAsync();
            return outbound.Id;
        }
    }
}
