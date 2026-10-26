using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.EFDbContext;
using CP6.Core.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [CurrentPermissionPostgresFact]
    public async Task Resource_source_HTTP_requires_current_original_owner()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get,
            "/api/mes/resource-sources/owner-v1/MACHINE"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [TenantManagementPostgresTheory]
    [InlineData("MACHINE")]
    [InlineData("WORK_CENTER")]
    public async Task Resource_source_native_uuid_exact_values_ordinary_patch_history_and_original_operation(string kind)
    {
        await using var h = await Seed();
        var authority = kind == "MACHINE" ? await GrantMachine(h) : await GrantCost(h);
        if (kind == "MACHINE") await SeedMachine(h); else await SeedCost(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority);
        var initial = Assert.Single(await service.ListAsync(kind));
        Assert.Equal("1", initial.Reference.Version);
        Assert.Equal("NOT_ATTESTED", initial.Qualification);
        if (kind == "WORK_CENTER") Assert.Equal("1234567890123.12345678", initial.Source.GetProperty("capability").GetProperty("dailyCapacityHours").GetString());
        else
        {
            Assert.Equal(480, initial.Source.GetProperty("capability").GetProperty("plannedRunMinutesPerDay").GetInt32());
            Assert.Equal(JsonValueKind.Null, initial.Source.GetProperty("capability").GetProperty("capacityPerHour").ValueKind);
        }
        var command = new ResourceSourceCommandV1(Guid.NewGuid(), initial.Reference, [new("name", "Updated original")]);
        var result = await service.SaveAsync(kind, command);
        Assert.True(result.Changed); Assert.Equal("2", result.Version);
        var after = await service.ReadAsync(kind, initial.Reference.Id);
        Assert.Equal(new ResourceSourceAxesV1("2", "1", "1", "1"), after.Axes);
        Assert.Equal("Updated original", after.Source.GetProperty("master").GetProperty("name").GetString());
        Assert.Equal(initial.OriginalJson, (await service.ReadAsync(kind, initial.Reference.Id, "1")).OriginalJson);
        Assert.Equal(result, await service.SaveAsync(kind, command));
        Assert.Equal(result, (await service.OperationAsync(kind, command.OperationId, false)).Result);
        var conflict = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveAsync(kind,
            command with { Patches = [new("name", "different")] }));
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", conflict.Message);
        var stale = await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveAsync(kind, command with { OperationId = Guid.NewGuid() }));
        Assert.Equal(412, stale.Status);
        var noop = await service.SaveAsync(kind, command with { OperationId = Guid.NewGuid(), Expected = after.Reference });
        Assert.False(noop.Changed); Assert.Equal("2", noop.Version);
        Assert.Equal("price-owner", kind == "MACHINE" ? await h.Db.Machines.Select(r => r.Modifier).SingleAsync() : await h.Db.WorkCenters.Select(r => r.Modifier).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_legacy_updates_capture_independent_axes_status_and_audit_are_noops()
    {
        await using var h = await Seed(); var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE"));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"Status\"=1,\"ModifyDate\"=LOCALTIMESTAMP WHERE \"TenantId\"={h.Tenant}");
        Assert.Equal(first.Reference, (await service.ReadAsync("MACHINE", first.Reference.Id)).Reference);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"CapacityPerHour\"=99999999.99,\"StandardCycleSec\"=0.0001 WHERE \"TenantId\"={h.Tenant}");
        var capacity = await service.ReadAsync("MACHINE", first.Reference.Id);
        Assert.Equal(new ResourceSourceAxesV1("1", "2", "1", "1"), capacity.Axes);
        Assert.Equal("99999999.99", capacity.Source.GetProperty("capability").GetProperty("capacityPerHour").GetString());
        Assert.Equal("0.0001", capacity.Source.GetProperty("capability").GetProperty("standardCycleSeconds").GetString());
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"WgCd\"='OLD',\"BaseCd\"='ALIAS' WHERE \"TenantId\"={h.Tenant}");
        Assert.Equal(new ResourceSourceAxesV1("1", "2", "2", "1"), (await service.ReadAsync("MACHINE", first.Reference.Id)).Axes);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"ActiveFlg\"=false WHERE \"TenantId\"={h.Tenant}");
        var disabled = await service.ReadAsync("MACHINE", first.Reference.Id);
        Assert.False(disabled.SourceEnabled); Assert.Equal(new ResourceSourceAxesV1("1", "2", "2", "2"), disabled.Axes);
        Assert.Equal("ALIAS", disabled.Source.GetProperty("assignment").GetProperty("legacyBaseCode").GetString());
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_patch_null_omission_date_and_controlled_fields_are_distinct()
    {
        await using var h = await Seed(); var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE"));
        await service.SaveAsync("MACHINE", new(Guid.NewGuid(), first.Reference, [new("remarks", "keep"), new("model", "M"), new("installDate", "2024-02-29")]));
        var next = await service.ReadAsync("MACHINE", first.Reference.Id);
        await service.SaveAsync("MACHINE", new(Guid.NewGuid(), next.Reference, [new("model", null)]));
        var last = await service.ReadAsync("MACHINE", first.Reference.Id);
        Assert.Equal("keep", last.Source.GetProperty("master").GetProperty("remarks").GetString());
        Assert.Equal(JsonValueKind.Null, last.Source.GetProperty("master").GetProperty("model").ValueKind);
        foreach (var patches in new ResourceSourcePatchV1[][] { [new("installDate", "2023-02-29")], [new("capacityPerHour", "1")],
            [new("enabled", "true")], [new("code", "NEW")], [new("name", "")], [new("name", "A"), new("name", "B")] })
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveAsync("MACHINE", new(Guid.NewGuid(), last.Reference, patches)));
        Assert.Equal(last.Reference, (await service.ReadAsync("MACHINE", first.Reference.Id)).Reference);
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_current_object_authority_applies_to_history_and_original_recovery()
    {
        await using var h = await Seed(); var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE"));
        var command = new ResourceSourceCommandV1(Guid.NewGuid(), first.Reference, [new("name", "private")]);
        await service.SaveAsync("MACHINE", command);
        await h.Db.Sys_RoleDataScopes.Where(r => r.ResourceKey == "mes-machine").ExecuteUpdateAsync(u => u.SetProperty(r => r.ScopeType, 1));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"Creator\"='someone-else' WHERE \"TenantId\"={h.Tenant}");
        Assert.Empty(await service.ListAsync("MACHINE"));
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync("MACHINE", first.Reference.Id, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.OperationAsync("MACHINE", command.OperationId, false))).Status);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"Creator\"='price-owner' WHERE \"TenantId\"={h.Tenant}");
        Assert.Equal("COMMITTED", (await service.OperationAsync("MACHINE", command.OperationId, false)).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_missing_trigger_mismatched_source_and_immutable_history_fail_closed()
    {
        await using var h = await Seed(); var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("DELETE FROM cp6_resource_source_v1.versions"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync("TRUNCATE public.\"M_Machine\""));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"M_Machine\" DISABLE TRIGGER resource_source_capture");
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync("MACHINE", first.Reference.Id))).Status);
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"Remarks\"='unobserved' WHERE \"TenantId\"={h.Tenant}");
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE public.\"M_Machine\" ENABLE TRIGGER resource_source_capture");
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync("MACHINE", first.Reference.Id, "1"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_unknown_close_prevents_late_write_and_HTTP_has_no_store()
    {
        await using var h = await Seed(); var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE")); var key = Guid.NewGuid();
        Assert.Equal("UNKNOWN", (await service.OperationAsync("MACHINE", key, false)).State);
        Assert.Equal("CLOSED", (await service.OperationAsync("MACHINE", key, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() =>
            service.SaveAsync("MACHINE", new(key, first.Reference, [new("name", "late")])))).Message);
        Assert.Equal(first.Reference, (await service.ReadAsync("MACHINE", first.Reference.Id)).Reference);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/mes/resource-sources/owner-v1/MACHINE"));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.True(read.Headers.CacheControl?.NoStore);
        using var request = OperationsRequest(cookies, HttpMethod.Post, "/api/mes/resource-sources/owner-v1/MACHINE/commands");
        request.Content = JsonContent.Create(new ResourceSourceCommandV1(Guid.NewGuid(), first.Reference, [new("name", "HTTP name")]));
        using var saved = await host.Send(request); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal("HTTP name", await h.Db.Machines.Select(row => row.MachineName).SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_commit_interruption_rolls_back_master_axes_audit_and_original()
    {
        var fault = new InterruptUomCommit(); await using var h = await Seed(fault);
        var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE"));
        var audits = await h.Db.Sys_FieldAuditLogs.CountAsync();
        var command = new ResourceSourceCommandV1(Guid.NewGuid(), first.Reference, [new("name", "rollback")]);
        fault.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveAsync("MACHINE", command))).Message);
        Assert.Equal(first.OriginalJson, (await service.ReadAsync("MACHINE", first.Reference.Id)).OriginalJson);
        Assert.Equal(audits, await h.Db.Sys_FieldAuditLogs.CountAsync());
        Assert.Equal(1, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_resource_source_v1.versions WHERE tenant_id={h.Tenant} AND resource_id={first.Reference.Id}
            """).SingleAsync());
        Assert.Equal("UNKNOWN", (await service.OperationAsync("MACHINE", command.OperationId, false)).State);
        Assert.Equal("CLOSED", (await service.OperationAsync("MACHINE", command.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.SaveAsync("MACHINE", command))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_competing_CAS_commits_one_successor_and_denies_another_tenant()
    {
        await using var h = await Seed(); var authority = await GrantCost(h); await SeedCost(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("WORK_CENTER"));
        await using var other = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = new ResourceSourceOwnerV1(other, new(other, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), CostCatalog().Capture));
        async Task<string> Save(ResourceSourceOwnerV1 target, string name)
        {
            try { await target.SaveAsync("WORK_CENTER", new(Guid.NewGuid(), first.Reference, [new("name", name)])); return "COMMITTED"; }
            catch (OrderPriceCorrectionException error) { Assert.Equal(412, error.Status); return error.Message; }
        }
        var results = await Task.WhenAll(Save(service, "A"), Save(peer, "B"));
        Assert.Single(results, r => r == "COMMITTED"); Assert.Single(results, r => r == "RESOURCE_SOURCE_CHANGED");
        Assert.Equal("2", (await service.ReadAsync("WORK_CENTER", first.Reference.Id)).Reference.Version);
        await using var foreign = await Seed(); var foreignAuthority = await GrantCost(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ResourceSourceOwnerV1(foreign.Db, foreignAuthority)
            .ReadAsync("WORK_CENTER", first.Reference.Id, "1"))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ResourceSourceOwnerV1(other, authority).ReadAsync("WORK_CENTER", first.Reference.Id))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_forward_migration_observes_existing_rows_without_rewriting_them_or_grants()
    {
        await using var h = await Seed();
        var settings = new NpgsqlConnectionStringBuilder(h.Db.Database.GetConnectionString()) { Database = "postgres" };
        var name = "resource_forward_" + Guid.NewGuid().ToString("N");
        await using (var root = new NpgsqlConnection(settings.ConnectionString))
        { await root.OpenAsync(); await using var command = new NpgsqlCommand("CREATE DATABASE " + name, root); await command.ExecuteNonQueryAsync(); }
        settings.Database = name; var database = new DatabaseOptions(DatabaseProvider.PostgreSql);
        var profile = DatabaseMigrationProfile.For(database, DatabaseContextKind.Core);
        var options = DatabaseContextOptions.Configure(new DbContextOptionsBuilder<CP6Context>(), database, settings.ConnectionString,
            profile.MigrationsAssembly, profile.HistoryTable, profile.HistorySchema);
        await using var db = new CP6Context(options.Options, new TenantContext());
        await db.GetService<IMigrator>().MigrateAsync("20261026210000_ConfigurationRuleV1");
        db.Machines.Add(new() { MachineCd = "old-Case", MachineName = "Historical", PlannedRunMinutesPerDay = 0, CapacityPerHour = null, Creator = "old" });
        db.WorkCenters.Add(new() { WgCd = "legacy", WgName = null, DailyCapacityHours = 1234567890123.12345678m, Enable = false });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var oldMachine = await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(p)::text AS \"Value\" FROM public.\"M_Machine\" p").SingleAsync();
        var oldCenter = await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(p)::text AS \"Value\" FROM public.\"T_WorkCenter\" p").SingleAsync();
        var grants = await db.Sys_RoleActions.CountAsync();
        await db.Database.MigrateAsync();
        Assert.Equal(oldMachine, await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(p)::text AS \"Value\" FROM public.\"M_Machine\" p").SingleAsync());
        Assert.Equal(oldCenter, await db.Database.SqlQueryRaw<string>("SELECT to_jsonb(p)::text AS \"Value\" FROM public.\"T_WorkCenter\" p").SingleAsync());
        Assert.Equal(grants, await db.Sys_RoleActions.CountAsync());
        Assert.Equal(2, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_resource_source_v1.versions WHERE revision=1 AND source_present").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM cp6_resource_source_v1.heads h JOIN cp6_resource_source_v1.versions v USING(tenant_id,kind,resource_id,revision) WHERE v.master_revision<>1").SingleAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Resource_source_hard_deleted_identity_is_not_disclosed_or_reused()
    {
        await using var h = await Seed(); var authority = await GrantMachine(h); await SeedMachine(h);
        var service = new ResourceSourceOwnerV1(h.Db, authority); var first = Assert.Single(await service.ListAsync("MACHINE"));
        await h.Db.Machines.Where(m => m.Id == first.Reference.Id).ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync("MACHINE", first.Reference.Id, "1"))).Status);
        Assert.False(await h.Db.Database.SqlQuery<bool>($"""
            SELECT v.source_present AS "Value" FROM cp6_resource_source_v1.heads h JOIN cp6_resource_source_v1.versions v
            USING(tenant_id,kind,resource_id,revision) WHERE h.tenant_id={h.Tenant} AND h.resource_id={first.Reference.Id}
            """).SingleAsync());
        h.Db.Machines.Add(new() { Id = first.Reference.Id, MachineCd = "OTHER", MachineName = "New identity" });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => h.Db.SaveChangesAsync());
        Assert.Contains("RESOURCE_IDENTITY_REUSED", error.InnerException!.Message); h.Db.ChangeTracker.Clear();
        Assert.Empty(await service.ListAsync("MACHINE"));
    }
}
