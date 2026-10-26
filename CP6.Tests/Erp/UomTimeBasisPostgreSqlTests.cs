using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static UomTimeBasisOwnerV1 TimeBases(Harness h, CP6.Core.EFDbContext.CP6Context? db = null) => new(db ?? h.Db, UnitAuthority(h, db));
    private static UomTimeBasisCommandV1 TimeCommand(UomProfileVersionV1 unit, string basis = "SECOND", string expected = "absent", bool enabled = true)
        => new(Guid.NewGuid(), expected, new(unit.Reference, basis, enabled, "Explicit original unit definition"));
    private static OperationsOwnerV1 ResourceUnitAuthority(Harness h)
    {
        var catalog = new AuthorizationCatalogStoreV1();
        foreach (var key in new[] { "mes-machine", "mes-work-center", "uom-profile" })
        { catalog.RegisterScope(key, key, [1, 2, 3, 4, 5], 5); catalog.RegisterFields(key, []); }
        catalog.Seal(); return new(h.Db, new OperationsHttp { HttpContext = h.Http }, _ => Task.FromResult(h.User), catalog.Capture);
    }
    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_HTTP_requires_original_unit_owner()
    {
        await using var h = await Seed();
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!);
        var cookies = await Login(host, h);
        using var response = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/uom-profiles/owner-v1/time-bases/scope"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_explicit_original_CAS_noop_history_and_recovery()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var unit = await MakeUnit(Units(h), BaseUnit("HOUR-LABEL", "tenant-time", 8)); var owner = TimeBases(h);
        Assert.Equal("absent", (await owner.EditorAsync(unit.Reference.Id)).ExpectedVersion);
        var command = TimeCommand(unit); var result = await owner.SaveAsync(command);
        var first = await owner.ReadAsync(unit.Reference.Id, null); Assert.True(first.CurrentApplicable); Assert.Equal("SECOND", first.Definition.Basis);
        Assert.Equal("1", result.Version); Assert.Equal(result, await owner.SaveAsync(command));
        Assert.Equal(result, (await owner.OperationAsync(command.OperationId, false)).Result);
        var noop = await owner.SaveAsync(command with { OperationId = Guid.NewGuid(), ExpectedVersion = "1" }); Assert.Equal("1", noop.Version);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(command with { Definition = command.Definition with { Basis = "HOUR" } }))).Message);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(TimeCommand(unit, "HOUR")))).Status);
        await owner.SaveAsync(TimeCommand(unit, "MINUTE", "1"));
        Assert.Equal(first.OriginalJson, (await owner.ReadAsync(unit.Reference.Id, "1")).OriginalJson);
        Assert.False((await owner.ReadAsync(unit.Reference.Id, "1")).CurrentApplicable);
        await owner.SaveAsync(TimeCommand(unit, "MINUTE", "2", false));
        Assert.Contains("TIME_BASIS_DISABLED", (await owner.ReadAsync(unit.Reference.Id, null)).Reasons!);
        Assert.Equal(result, (await owner.OperationAsync(command.OperationId, false)).Result);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_does_not_guess_labels_and_preserves_exact_resource_values()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantMachine(h); await SeedMachine(h);
        var unit = await MakeUnit(Units(h), BaseUnit("HOUR", "TIME", 8)); var authority = ResourceUnitAuthority(h);
        var source = Assert.Single(await new ResourceSourceOwnerV1(h.Db, authority).ListAsync("MACHINE")); var service = new ResourceTimeOwnerV1(h.Db, authority);
        var request = new ResourceTimeRequestV1(source.Reference, "plannedRunMinutesPerDay", unit.Reference);
        var missing = await service.ReadAsync("MACHINE", source.Reference.Id, request); Assert.Equal("UNKNOWN", missing.State); Assert.Null(missing.ConvertedQuantity);
        Assert.Contains("TIME_BASIS_REQUIRED", missing.Reasons); Assert.Equal("480", missing.NativeQuantity);
        var definition = await TimeBases(h).SaveAsync(TimeCommand(unit, "HOUR"));
        var converted = await service.ReadAsync("MACHINE", source.Reference.Id, request); Assert.Equal("8.00000000", converted.ConvertedQuantity);
        Assert.Equal("NOT_ATTESTED", converted.Qualification); Assert.Equal("CURRENT_TRANSACTION_ONLY", converted.Observation);
        Assert.Equal(definition.Version, converted.TimeBasisRef!.Version);
        await TimeBases(h).SaveAsync(TimeCommand(unit, "SECOND", "1"));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => service.ReadAsync("MACHINE", source.Reference.Id, request with { ExpectedBasis = converted.TimeBasisRef }))).Status);
        Assert.Equal("28800.00000000", (await service.ReadAsync("MACHINE", source.Reference.Id, request)).ConvertedQuantity);
        Assert.Equal("HOUR", (await Units(h).ReadAsync(unit.Reference.Id, null)).Profile.Code);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_ratio_exactness_null_and_stale_resource_never_default_or_round()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantMachine(h); await SeedMachine(h);
        var units = Units(h); var second = await MakeUnit(units, BaseUnit("SEC", "TIME", 8)); await TimeBases(h).SaveAsync(TimeCommand(second));
        var minute = await MakeUnit(units, BaseUnit("MIN", "TIME", 8) with { Mode = "RATIO", BaseRef = second.Reference, Numerator = "60" });
        var authority = ResourceUnitAuthority(h); var originals = new ResourceSourceOwnerV1(h.Db, authority); var time = new ResourceTimeOwnerV1(h.Db, authority);
        var source = Assert.Single(await originals.ListAsync("MACHINE")); var request = new ResourceTimeRequestV1(source.Reference, "standardCycleSeconds", minute.Reference);
        var missing = await time.ReadAsync("MACHINE", source.Reference.Id, request); Assert.Equal("UNKNOWN", missing.State); Assert.Null(missing.NativeQuantity);
        await h.Db.Machines.ExecuteUpdateAsync(u => u.SetProperty(m => m.StandardCycleSec, 0.0001m));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => time.ReadAsync("MACHINE", source.Reference.Id, request))).Status);
        source = await originals.ReadAsync("MACHINE", source.Reference.Id);
        Assert.Equal("UOM_CONVERSION_NOT_EXACT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => time.ReadAsync("MACHINE", source.Reference.Id, request with { SourceRef = source.Reference }))).Message);
        var exact = await time.ReadAsync("MACHINE", source.Reference.Id, new(source.Reference, "standardCycleSeconds", second.Reference));
        Assert.Equal("0.00010000", exact.ConvertedQuantity);
        await h.Db.Machines.ExecuteUpdateAsync(u => u.SetProperty(m => m.ActiveFlg, false)); source = await originals.ReadAsync("MACHINE", source.Reference.Id);
        Assert.Contains("RESOURCE_SOURCE_DISABLED", (await time.ReadAsync("MACHINE", source.Reference.Id, new(source.Reference, "standardCycleSeconds", second.Reference))).Reasons);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_work_center_large_capacity_and_base_profile_change_are_exact()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantCost(h); await SeedCost(h);
        var units = Units(h); var hour = await MakeUnit(units, BaseUnit("H", "T", 8)); var owner = TimeBases(h); await owner.SaveAsync(TimeCommand(hour, "HOUR"));
        var authority = ResourceUnitAuthority(h); var source = Assert.Single(await new ResourceSourceOwnerV1(h.Db, authority).ListAsync("WORK_CENTER"));
        var service = new ResourceTimeOwnerV1(h.Db, authority);
        Assert.Equal("1234567890123.12345678", (await service.ReadAsync("WORK_CENTER", source.Reference.Id, new(source.Reference, "dailyCapacityHours", hour.Reference))).ConvertedQuantity);
        await units.CommandAsync(Revise(hour, hour.Profile with { Name = "New unit revision" })); hour = await units.ReadAsync(hour.Reference.Id, null);
        Assert.False((await owner.ReadAsync(hour.Reference.Id, null)).CurrentApplicable);
        Assert.Contains("TIME_BASIS_SOURCE_CHANGED", (await service.ReadAsync("WORK_CENTER", source.Reference.Id, new(source.Reference, "dailyCapacityHours", hour.Reference))).Reasons);
        await owner.SaveAsync(TimeCommand(hour, "HOUR", "1"));
        Assert.Equal("KNOWN", (await service.ReadAsync("WORK_CENTER", source.Reference.Id, new(source.Reference, "dailyCapacityHours", hour.Reference))).State);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_current_unit_and_resource_authorities_cannot_be_borrowed()
    {
        await using var h = await Seed(); await GrantUomProfile(h); await GrantMachine(h); await SeedMachine(h);
        var unit = await MakeUnit(Units(h)); var command = TimeCommand(unit); var receipt = await TimeBases(h).SaveAsync(command);
        var authority = ResourceUnitAuthority(h); var source = Assert.Single(await new ResourceSourceOwnerV1(h.Db, authority).ListAsync("MACHINE"));
        var menu = await h.Db.Sys_Menus.Where(m => m.MenuKey == "erp-uom-profile").Select(m => m.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(a => a.MenuId == menu && a.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TimeBases(h).ReadAsync(unit.Reference.Id, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TimeBases(h).OperationAsync(command.OperationId, false))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new ResourceTimeOwnerV1(h.Db, authority)
            .ReadAsync("MACHINE", source.Reference.Id, new(source.Reference, "plannedRunMinutesPerDay", unit.Reference)))).Status);
        await using var foreign = await Seed(); await GrantUomProfile(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TimeBases(foreign).ReadAsync(receipt.ObjectId, "1"))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_failure_rolls_back_head_original_audit_and_closes_unknown()
    {
        var fault = new InterruptUomCommit(); await using var h = await Seed(fault); await GrantUomProfile(h);
        var unit = await MakeUnit(Units(h)); var command = TimeCommand(unit); var owner = TimeBases(h); fault.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(command))).Message);
        Assert.Equal("absent", (await owner.EditorAsync(unit.Reference.Id)).ExpectedVersion);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_uom_time_v1.versions WHERE tenant_id={h.Tenant}
            """).SingleAsync());
        Assert.Equal("UNKNOWN", (await owner.OperationAsync(command.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync(command.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(command))).Message);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_real_HTTP_rejects_implicit_enabled_and_reads_original_result()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var unit = await MakeUnit(Units(h)); var command = TimeCommand(unit);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        using var request = OperationsRequest(cookies, HttpMethod.Post, "/api/uom-profiles/owner-v1/time-bases/commands"); request.Content = JsonContent.Create(command);
        using var saved = await host.Send(request); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.True(saved.Headers.CacheControl?.NoStore);
        using var bad = OperationsRequest(cookies, HttpMethod.Post, "/api/uom-profiles/owner-v1/time-bases/commands");
        bad.Content = JsonContent.Create(new { operationId = Guid.NewGuid(), expectedVersion = "1", definition = new { baseRef = unit.Reference, basis = "HOUR", reason = "omitted enable" } });
        using var rejected = await host.Send(bad); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        using var read = await host.Send(OperationsRequest(cookies, HttpMethod.Get, "/api/uom-profiles/owner-v1/time-bases/operations/" + command.OperationId));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode); Assert.Contains("COMMITTED", await read.Content.ReadAsStringAsync());
        await GrantMachine(h); await SeedMachine(h);
        var source = Assert.Single(await new ResourceSourceOwnerV1(h.Db, ResourceUnitAuthority(h)).ListAsync("MACHINE"));
        using var conversion = OperationsRequest(cookies, HttpMethod.Post, "/api/mes/resource-sources/owner-v1/MACHINE/" + source.Reference.Id + "/time-values");
        conversion.Content = JsonContent.Create(new ResourceTimeRequestV1(source.Reference, "plannedRunMinutesPerDay", unit.Reference));
        using var converted = await host.Send(conversion); Assert.Equal(HttpStatusCode.OK, converted.StatusCode); Assert.True(converted.Headers.CacheControl?.NoStore);
        Assert.Contains("28800.000000000000000000", await converted.Content.ReadAsStringAsync());
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_concurrent_CAS_commits_one_original_and_history_is_immutable()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var unit = await MakeUnit(Units(h));
        var owner = TimeBases(h); await owner.SaveAsync(TimeCommand(unit));
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        async Task<string> Save(UomTimeBasisOwnerV1 service, string basis)
        {
            try { await service.SaveAsync(TimeCommand(unit, basis, "1")); return "COMMITTED"; }
            catch (OrderPriceCorrectionException e) { Assert.Equal(412, e.Status); return e.Message; }
        }
        var results = await Task.WhenAll(Save(owner, "MINUTE"), Save(TimeBases(h, peer), "HOUR"));
        Assert.Single(results, r => r == "COMMITTED"); Assert.Single(results, r => r == "TIME_BASIS_CHANGED");
        Assert.Equal("2", (await owner.ReadAsync(unit.Reference.Id, null)).Reference.Version);
        Assert.Equal(2, await h.Db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM cp6_main_owner_v1.operations WHERE tenant_id={h.Tenant} AND owner='Main.Erp.UomTimeBasis.v1'
            """).SingleAsync());
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE cp6_uom_time_v1.versions SET actor_id={Guid.NewGuid()} WHERE tenant_id={h.Tenant} AND profile_id={unit.Reference.Id}
            """));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(() => h.Db.Database.ExecuteSqlInterpolatedAsync($"""
            DELETE FROM cp6_uom_time_v1.heads WHERE tenant_id={h.Tenant} AND profile_id={unit.Reference.Id}
            """));
        Assert.Equal("SECOND", (await owner.ReadAsync(unit.Reference.Id, "1")).Definition.Basis);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_rejects_ratio_as_base_stale_versions_and_unfenced_contexts()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var units = Units(h); var unit = await MakeUnit(units);
        var ratio = await MakeUnit(units, BaseUnit("PACK", scale: 18) with { Mode = "RATIO", BaseRef = unit.Reference, Numerator = "2" });
        Assert.Equal("TIME_BASIS_BASE_REQUIRED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TimeBases(h).SaveAsync(TimeCommand(ratio)))).Message);
        await units.CommandAsync(Revise(unit, unit.Profile with { Enabled = false }));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => TimeBases(h).SaveAsync(TimeCommand(unit)))).Status);
        var authority = UnitAuthority(h);
        await using var peer = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => new UomTimeBasisOwnerV1(peer, authority).ReadAsync(unit.Reference.Id, null))).Status);
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => authority.ExecuteAsync(OperationsOwnerV1.UomProfile, "query", false,
            (_, token) => new UomTimeBasisStoreV1(h.Db).Read(unit.Reference.Id, null, token)))).Status);
    }

    [CurrentPermissionPostgresFact]
    public async Task Uom_time_basis_current_department_scope_fences_write_history_and_receipts()
    {
        await using var h = await Seed(); await GrantUomProfile(h); var units = Units(h); var admin = await MappingAdmin(h);
        var unit = await MakeUnit(units); var owner = TimeBases(h); var command = TimeCommand(unit); await owner.SaveAsync(command);
        await StandaloneScope(h, admin, 2);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(TimeCommand(unit, "HOUR", "1")))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync(unit.Reference.Id, "1"))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync(command.OperationId, false))).Status);
        var allowedCommand = await NewUnit(units, BaseUnit("DEPT-SEC"), admin.Child); await units.CommandAsync(allowedCommand);
        var allowed = await units.ReadAsync(allowedCommand.ProfileId, null); await owner.SaveAsync(TimeCommand(allowed));
        Assert.True((await owner.ReadAsync(allowed.Reference.Id, null)).CurrentApplicable);
    }
}
