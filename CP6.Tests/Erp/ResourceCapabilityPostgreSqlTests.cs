using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Mes;
using CP6.Core.Services.Plm;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CP6.Core.Services.Common;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    private static async Task<(ResourceCapabilityOwnerV1 Owner, ResourceCapabilityCommandV1 Input, UomProfileVersionV1 Unit)> CapabilitySetup(Harness h, string kind = "MACHINE")
    {
        if (kind == "MACHINE") { await GrantMachine(h); await SeedMachine(h); }
        else { await GrantCost(h); await SeedCost(h); }
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "uom-profile")) await GrantUomProfile(h);
        await EngineeringAction(h, kind == "MACHINE" ? "mes-machine" : "mes-work-center", "capability-edit");
        var owner = RoutingAuthority(h); var resources = new ResourceSourceOwnerV1(h.Db, owner);
        var source = Assert.Single(await resources.ListAsync(kind)); var unit = await MakeUnit(Units(h), BaseUnit("MM", "LENGTH", 8));
        var item = new ResourceCapabilityItemV1("WIDTH", "DECIMAL", "9999999999999.12345678", null, unit.Reference, "LENGTH",
            "2026-10-01T08:00:00.123456+08:00", "2027-01-01T00:00:00Z", "Explicit operator declaration pending physical verification");
        return (new(h.Db, owner), new(Guid.NewGuid(), "absent", new(source.Reference, null, null, null,
            kind == "MACHINE" ? [item] : [], "Original source and explicit declaration"), "First resource capability record"), unit);
    }
    [TenantManagementPostgresTheory]
    [InlineData("MACHINE")]
    [InlineData("WORK_CENTER")]
    public async Task ResourceCapability_native_history_exact_values_noop_and_original_recovery(string kind)
    {
        await using var h = await Seed(); var (owner, input, _) = await CapabilitySetup(h, kind);
        var before = await new ResourceSourceOwnerV1(h.Db, RoutingAuthority(h)).ReadAsync(kind, input.Body.Source.Id);
        var result = await owner.SaveAsync(kind, input); var saved = await owner.ReadAsync(kind, result.ObjectId);
        Assert.Equal(result, await owner.SaveAsync(kind, input)); Assert.Equal(result, (await owner.OperationAsync(kind, input.OperationId, false)).Result);
        Assert.Equal("DECLARED_NOT_QUALIFIED", saved.Qualification); Assert.Contains("RESOURCE_QUALIFICATION_REQUIRED", saved.Missing);
        if (kind == "MACHINE") Assert.Equal("9999999999999.12345678", Assert.Single(saved.Body.Capabilities).DecimalValue);
        var noop = await owner.SaveAsync(kind, input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1" });
        Assert.False(noop.Changed); Assert.Equal("1", noop.Version);
        var revised = input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", Body = input.Body with { Basis = "Explicit later declaration" } };
        Assert.Equal("2", (await owner.SaveAsync(kind, revised)).Version);
        Assert.Equal(saved.OriginalJson, (await owner.ReadAsync(kind, result.ObjectId, "1")).OriginalJson);
        Assert.False((await owner.ReadAsync(kind, result.ObjectId, "1")).IsCurrent);
        Assert.Equal(before.OriginalJson, (await new ResourceSourceOwnerV1(h.Db, RoutingAuthority(h)).ReadAsync(kind, result.ObjectId)).OriginalJson);
        Assert.Equal("ORIGINAL_OPERATION_INPUT_CONFLICT", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(kind, input with { Reason = "Different original" }))).Message);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync(kind, revised with { OperationId = Guid.NewGuid() }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_precise_closed_inputs_and_unknown_are_not_physical_qualification()
    {
        await using var h = await Seed(); var (owner, input, unit) = await CapabilitySetup(h); var item = input.Body.Capabilities[0];
        foreach (var invalid in new[] { "10000000000000.00000000", "1e2", "1.000000001", "NaN", "01.00000000" })
            Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with
                { Body = input.Body with { Capabilities = [item with { DecimalValue = invalid }] } }))).Status);
        foreach (var invalid in new[] { item with { EnumValue = "BOTH" }, item with { Unit = null }, item with { ValidTo = item.ValidFrom },
            item with { ValidFrom = "2026-10-01T08:00:00" }, item with { ValidFrom = "2026-10-01T08:00:00.1234567Z" } })
            await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with { Body = input.Body with { Capabilities = [invalid] } }));
        await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with { Body = input.Body with { Capabilities = [item, item] } }));
        Assert.Equal("RESOURCE_CAPABILITY_DIMENSION_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with
            { Body = input.Body with { Capabilities = [item with { Dimension = "COUNT" }] } }))).Message);
        var coarse = await MakeUnit(Units(h), BaseUnit("MM2", "LENGTH", 2));
        Assert.Equal("RESOURCE_CAPABILITY_UNIT_PRECISION_MISMATCH", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with
            { Body = input.Body with { Capabilities = [item with { Unit = coarse.Reference }] } }))).Message);
        input = input with { Body = input.Body with { Capabilities = [] } }; await owner.SaveAsync("MACHINE", input);
        var saved = await owner.ReadAsync("MACHINE", input.Body.Source.Id);
        Assert.Contains("RESOURCE_CAPABILITIES_UNKNOWN", saved.Missing); Assert.Contains("RESOURCE_TIME_UNIT_REQUIRED", saved.Missing);
        Assert.Equal(480, (await owner.EditorAsync("MACHINE", input.Body.Source.Id)).Resource.Source.GetProperty("capability").GetProperty("plannedRunMinutesPerDay").GetInt32());
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_time_basis_and_current_unit_changes_preserve_original_but_block_new_use()
    {
        await using var h = await Seed(); var (owner, input, _) = await CapabilitySetup(h);
        var unit = await MakeUnit(Units(h), BaseUnit("SEC", "TIME", 8)); var time = new UomTimeBasisOwnerV1(h.Db, RoutingAuthority(h));
        await time.SaveAsync(new(Guid.NewGuid(), "absent", new(unit.Reference, "SECOND", true, "Explicit physical time meaning")));
        var basis = await time.ReadAsync(unit.Reference.Id, null);
        input = input with { Body = input.Body with { TimeUnit = unit.Reference, TimeBasis = basis.Reference } };
        await owner.SaveAsync("MACHINE", input); var saved = await owner.ReadAsync("MACHINE", input.Body.Source.Id);
        Assert.DoesNotContain("RESOURCE_TIME_UNIT_REQUIRED", saved.Missing);
        await time.SaveAsync(new(Guid.NewGuid(), basis.Reference.Version, basis.Definition with { Enabled = false }));
        var changed = await owner.ReadAsync("MACHINE", input.Body.Source.Id);
        Assert.Equal(saved.OriginalJson, changed.OriginalJson); Assert.Contains("TIME_BASIS_CHANGED_OR_DISABLED", changed.Missing);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1" }))).Status);
        Assert.Equal("COMMITTED", (await owner.OperationAsync("MACHINE", input.OperationId, false)).State);
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_native_source_changes_and_disabled_units_are_observed_without_rewriting_history()
    {
        await using var h = await Seed(); var (owner, input, unit) = await CapabilitySetup(h);
        await owner.SaveAsync("MACHINE", input); var saved = await owner.ReadAsync("MACHINE", input.Body.Source.Id);
        await Units(h).CommandAsync(Revise(unit, unit.Profile with { Enabled = false }));
        await h.Db.Database.ExecuteSqlInterpolatedAsync($"UPDATE public.\"M_Machine\" SET \"CapacityPerHour\"=12.34 WHERE \"TenantId\"={h.Tenant}");
        var stale = await owner.ReadAsync("MACHINE", input.Body.Source.Id);
        Assert.Equal(saved.OriginalJson, stale.OriginalJson); Assert.Contains("UNIT_SOURCE_CHANGED_OR_DISABLED", stale.Missing);
        Assert.Contains("RESOURCE_SOURCE_CHANGED_OR_DISABLED", stale.Missing);
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1" }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_current_permissions_tenant_and_original_disclosure_are_not_inherited_from_write()
    {
        await using var h = await Seed(); var (owner, input, _) = await CapabilitySetup(h); await owner.SaveAsync("MACHINE", input);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "mes-machine").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "capability-edit").ExecuteDeleteAsync();
        Assert.Equal("COMMITTED", (await owner.OperationAsync("MACHINE", input.OperationId, false)).State);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1" }))).Status);
        var unitMenu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "erp-uom-profile").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == unitMenu && x.ActionCode == "query").ExecuteDeleteAsync();
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.OperationAsync("MACHINE", input.OperationId, false))).Status);
        await using var foreign = await Seed(); var (other, _, _) = await CapabilitySetup(foreign);
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => other.ReadAsync("MACHINE", input.Body.Source.Id))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_commit_interruption_rolls_back_all_versions_and_close_wins_late_write()
    {
        var failure = new InterruptCurrencyCommit(); await using var h = await Seed(failure); var (owner, input, _) = await CapabilitySetup(h);
        failure.Interrupt = true;
        Assert.Equal("OPERATIONS_OUTCOME_UNKNOWN", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input))).Message);
        Assert.Null((await owner.EditorAsync("MACHINE", input.Body.Source.Id)).Current);
        Assert.Equal("UNKNOWN", (await owner.OperationAsync("MACHINE", input.OperationId, false)).State);
        Assert.Equal("CLOSED", (await owner.OperationAsync("MACHINE", input.OperationId, true)).State);
        Assert.Equal("ORIGINAL_OPERATION_CLOSED", (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input))).Message);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_resource_capability_v1.versions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_database_originals_and_capture_guards_fail_closed()
    {
        await using var h = await Seed(); var (owner, input, _) = await CapabilitySetup(h); await owner.SaveAsync("MACHINE", input);
        foreach (var sql in new[] { "DELETE FROM cp6_resource_capability_v1.versions", "TRUNCATE cp6_resource_capability_v1.versions CASCADE",
            "UPDATE cp6_resource_capability_v1.versions SET actor_id=gen_random_uuid()", "DELETE FROM cp6_resource_capability_v1.heads", "UPDATE cp6_resource_capability_v1.heads SET revision=revision+2" })
            await Assert.ThrowsAsync<PostgresException>(() => h.Db.Database.ExecuteSqlRawAsync(sql));
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_resource_capability_v1.versions DISABLE TRIGGER resource_capability_complete");
        Assert.Equal(503, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.ReadAsync("MACHINE", input.Body.Source.Id))).Status);
        await h.Db.Database.ExecuteSqlRawAsync("ALTER TABLE cp6_resource_capability_v1.versions ENABLE TRIGGER resource_capability_complete");
        Assert.NotNull(await owner.ReadAsync("MACHINE", input.Body.Source.Id));
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_routing_pins_exact_native_version_and_keeps_old_original_unqualified()
    {
        await using var h = await Seed(); var (owner, input, _) = await CapabilitySetup(h); await owner.SaveAsync("MACHINE", input);
        var capability = await owner.ReadAsync("MACHINE", input.Body.Source.Id); var parent = await RoutingParent(h); var route = RoutingCreate(parent);
        route = route with { Body = route.Body with { Operations = [route.Body.Operations[0] with { Resources = [input.Body.Source], ResourceCapabilities = [capability.Reference] }] } };
        var routes = Routings(h); await routes.CommandAsync(route); var before = await routes.ReadAsync(route.RoutingId);
        Assert.Contains(before.Missing!, x => x.StartsWith("RESOURCE_QUALIFICATION_REQUIRED:"));
        await owner.SaveAsync("MACHINE", input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", Body = input.Body with { Basis = "Second declaration" } });
        var after = await routes.ReadAsync(route.RoutingId); Assert.Equal(before.OriginalJson, after.OriginalJson);
        Assert.Contains(after.Missing!, x => x.StartsWith("RESOURCE_CAPABILITY_CHANGED:"));
        Assert.Equal(412, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => routes.CommandAsync(RoutingRevise(after)))).Status);
        await SeedMachine(h, "SECOND"); var other = (await new ResourceSourceOwnerV1(h.Db, RoutingAuthority(h)).ListAsync("MACHINE")).Single(x => x.Reference.Id != input.Body.Source.Id);
        Assert.Equal(400, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => routes.CommandAsync(RoutingRevise(after) with
            { Body = after.Body with { Operations = [after.Body.Operations[0] with { Resources = [other.Reference] }] } }))).Status);
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_actual_HTTP_save_history_close_and_permission_revocation()
    {
        await using var h = await Seed(); var (_, input, _) = await CapabilitySetup(h);
        await using var host = await AuthenticationApiProcess.Start(h.Db.Database.GetConnectionString()!); var cookies = await Login(host, h);
        var path = "/api/mes/resource-capabilities/owner-v1/MACHINE";
        using var request = OperationsRequest(cookies, HttpMethod.Post, path + "/commands"); request.Content = JsonContent.Create(input);
        using var saved = await host.Send(request); Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.True(saved.Headers.CacheControl?.NoStore);
        using var current = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "/" + input.Body.Source.Id + "/editor"));
        Assert.Equal(HttpStatusCode.OK, current.StatusCode); Assert.Contains("DECLARED_NOT_QUALIFIED", await current.Content.ReadAsStringAsync());
        var key = Guid.NewGuid(); using var close = await host.Send(OperationsRequest(cookies, HttpMethod.Post, path + "/operations/" + key + "/close"));
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);
        using var bad = OperationsRequest(cookies, HttpMethod.Post, path + "/commands");
        bad.Content = JsonContent.Create(new { input.OperationId, input.ExpectedVersion, input.Body, input.Reason, qualification = "QUALIFIED" });
        using var invalid = await host.Send(bad); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var menu = await h.Db.Sys_Menus.Where(x => x.MenuKey == "mes-machine").Select(x => x.MenuId).SingleAsync();
        await h.Db.Sys_RoleActions.Where(x => x.MenuId == menu && x.ActionCode == "query").ExecuteDeleteAsync();
        using var denied = await host.Send(OperationsRequest(cookies, HttpMethod.Get, path + "/operations/" + input.OperationId));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.True(denied.Headers.CacheControl?.NoStore);
    }

    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_competing_CAS_and_cancel_commit_have_one_durable_original()
    {
        await using var h = await Seed(); var (owner, input, _) = await CapabilitySetup(h);
        await using var db = server.Context(new TenantContext { CurrentTenantId = h.Tenant });
        var peer = new ResourceCapabilityOwnerV1(db, RoutingAuthority(h, db));
        async Task<string> Save(ResourceCapabilityOwnerV1 target, ResourceCapabilityCommandV1 command)
        {
            try { await target.SaveAsync("MACHINE", command); return "COMMITTED"; }
            catch (OrderPriceCorrectionException error) { Assert.True(error.Status is 409 or 412); return error.Message; }
        }
        var results = await Task.WhenAll(Save(owner, input), Save(peer, input with { OperationId = Guid.NewGuid(), Body = input.Body with { Basis = "Competing declaration" } }));
        Assert.Single(results, x => x == "COMMITTED"); Assert.Single(results, x => x == "RESOURCE_CAPABILITY_CHANGED");
        var race = input with { OperationId = Guid.NewGuid(), ExpectedVersion = "1", Body = input.Body with { Basis = "Commit versus close" } };
        var write = Save(owner, race); var close = peer.OperationAsync("MACHINE", race.OperationId, true);
        await Task.WhenAll(write, close); var actual = await owner.OperationAsync("MACHINE", race.OperationId, false);
        Assert.Equal(close.Result, actual);
        Assert.Equal(actual.State == "COMMITTED" ? "COMMITTED" : "ORIGINAL_OPERATION_CLOSED", write.Result);
        Assert.Equal(actual.State == "COMMITTED" ? "2" : "1", (await owner.ReadAsync("MACHINE", input.Body.Source.Id)).Reference.Version);
    }
    private sealed class CapabilityRevocation : DbCommandInterceptor
    {
        internal bool Armed;
        public override async ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken ct = default)
        {
            if (Armed && command.CommandText.Contains("INSERT INTO cp6_main_owner_v1.operations"))
            {
                Armed = false;
                await using var revoke = new NpgsqlCommand("DELETE FROM public.\"Sys_RoleAction\" WHERE \"MenuId\" IN (SELECT \"MenuId\" FROM public.\"Sys_Menus\" WHERE \"MenuKey\"='erp-uom-profile') AND \"ActionCode\"='query'",
                    (NpgsqlConnection)command.Connection!, (NpgsqlTransaction)command.Transaction!);
                await revoke.ExecuteNonQueryAsync(ct);
            }
            return result;
        }
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_final_nested_source_revocation_rolls_back_declaration_and_receipt()
    {
        var hook = new CapabilityRevocation(); await using var h = await Seed(hook); var (owner, input, _) = await CapabilitySetup(h);
        hook.Armed = true;
        Assert.Equal(403, (await Assert.ThrowsAsync<OrderPriceCorrectionException>(() => owner.SaveAsync("MACHINE", input))).Status);
        Assert.False(hook.Armed); Assert.Equal("UNKNOWN", (await owner.OperationAsync("MACHINE", input.OperationId, false)).State);
        Assert.Null((await owner.EditorAsync("MACHINE", input.Body.Source.Id)).Current);
        Assert.Equal(0, await h.Db.Database.SqlQuery<int>($"SELECT count(*)::integer AS \"Value\" FROM cp6_resource_capability_v1.versions WHERE tenant_id={h.Tenant}").SingleAsync());
    }
    [CurrentPermissionPostgresFact]
    public async Task ResourceCapability_source_check_keeps_native_original_and_never_promotes_declaration_to_PASS()
    {
        await using var h = await Seed(); var fixture = await CheckSetup(h); var (owner, input, _) = await CapabilitySetup(h);
        await owner.SaveAsync("MACHINE", input); var declared = await owner.ReadAsync("MACHINE", input.Body.Source.Id);
        foreach (var action in new[] { "query", "add", "edit" }) await EngineeringAction(h, "plm-engineering-routing", action);
        if (!await h.Db.Sys_RoleDataScopes.AnyAsync(x => x.ResourceKey == "plm-engineering-routing"))
            h.Db.Sys_RoleDataScopes.Add(new() { RoleId = 72, ResourceKey = "plm-engineering-routing", ScopeType = 5 });
        await h.Db.SaveChangesAsync(); h.Db.ChangeTracker.Clear();
        var revisions = new EngineeringRevisionOwnerV1(h.Db, CheckAuthority(h), Documents(h, fixture.Files), Specifications(h, fixture.Files));
        var parent = await revisions.ReadAsync(fixture.Command.Revision.Id, null); var route = RoutingCreate(parent);
        route = route with { Body = route.Body with { Operations = [route.Body.Operations[0] with { Resources = [input.Body.Source], ResourceCapabilities = [declared.Reference] }] } };
        var routes = Routings(h); await routes.CommandAsync(route); var routing = await routes.ReadAsync(route.RoutingId);
        await revisions.CommandAsync(PatchRevision(parent, new EngineeringDraftPatchV1 { Op = "UPSERT_COMPONENT",
            Component = new(Guid.NewGuid(), "ROUTING", "/manufacture", 10, "APPLIES", "", "Explicit declaration reference", routing.Reference) }));
        parent = await revisions.ReadAsync(parent.Reference.Id, null);
        var checks = Checks(h, fixture.Files); var result = await checks.CheckAsync(fixture.Command with { Revision = parent.Reference, Candidate = parent.CandidateRef, ExpectedEtag = parent.Etag });
        var view = await checks.ReadAsync(result.CheckRunId); Assert.NotEqual("PASS", view.Original.State);
        var source = Assert.Single(view.Original.Sources, x => x.Reference.Owner == ResourceCapabilityValuesV1.Owner);
        Assert.Equal(declared.OriginalJson, source.OriginalJson); Assert.Contains("RESOURCE_QUALIFICATION_REQUIRED", source.Findings);
    }
}
