using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Persistence;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("pause-first")]
    [InlineData("business-first")]
    [InlineData("permission-first")]
    public async Task PGT017_space_cad_provider_replacement_uses_current_manage_permission(string order)
    {
        const string manage = "model:provider:manage";
        const string ledger = "Space_IdempotencyRecord";
        var businessFirst = order == "business-first";
        var permissionFirst = order == "permission-first";
        await using var writer = await Seed();
        var spaceMenu = await GrantSpaceDesignActions(writer, "model:read", manage);
        await using var administrator = permissionFirst ? null : await TenantManagementSeed();
        if (permissionFirst)
        {
            var menus = await writer.Db.Sys_RoleActions.AsNoTracking().Where(action => action.RoleId == 72)
                .Select(action => action.MenuId).Distinct().ToArrayAsync();
            foreach (var menu in menus)
                writer.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menu });
            await writer.Db.SaveChangesAsync();
            writer.Db.ChangeTracker.Clear();
            await SeedFenceAdministrator(writer);
        }

        var connection = writer.Db.Database.GetConnectionString()!;
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, new SpaceDesignExecution(writer.Tenant, writer.User));
        await space.Database.MigrateAsync();
        var history = await SeedHistoricalProvider(space, writer, site);
        var settings = SpaceDesignSettings(writer.Tenant, site);
        if (permissionFirst)
        {
            foreach (var pair in IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(writer.Tenant))
                settings[pair.Key] = pair.Value;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("CP6_SPACE_CAD_PROVIDER_HTTP_PORT"), out var port))
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = permissionFirst
            ? await Login(host, "fence-manager", "Fence1!Password", writer.Tenant.ToString("N"))
            : await Login(host, administrator!);
        var path = $"{SpaceDesignPath}/sites/{site:D}";
        var body = new ReplaceSpaceCadProviderConfigurationRequest(1, "Simulated Site CAD provider disable", []);
        var key = Guid.NewGuid().ToString("N");
        var initial = await ReadCurrent();
        Assert.Equal(1, initial.ConfigurationRevision);
        Assert.Equal("historical.local", initial.Primary?.ProviderKey);
        Assert.Equal(writer.User, initial.UpdatedBy);

        IamRolePolicyCommandV1? revoke = null;
        TenantManagementCommandV1? suspend = null;
        if (permissionFirst)
            revoke = await RevokeActions(host, manager, manage);
        else
        {
            using var active = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
            var snapshot = await NumberHttpData<TenantManagementSnapshotV1>(active);
            suspend = new(Guid.NewGuid().ToString("N"), snapshot.Revision, "suspend", null);
        }

        await using var control = new NpgsqlConnection(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await barrier.OpenAsync();
        await observer.OpenAsync();
        var before = await ReadProviderWriteSet(observer, writer.Tenant, site);
        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage> authority;
        FenceWait providerWait, authorityWait;
        bool siteLockHeld;
        ProviderWriteSet firstCommit;
        // Hold the exact original-result write to detect the unfenced route on the baseline.
        await using (var holdReceipt = await HoldUnderlayTable(barrier, ledger))
        {
            if (businessFirst)
            {
                await using var holdTenant = await HoldUnderlayTenant(control, writer.Tenant);
                pending = Replace(body, key);
                providerWait = await WaitForFence(observer, barrier.ProcessID, $"%INSERT%{ledger}%", null, pending);
                siteLockHeld = await HoldsProviderSiteLock(observer, providerWait.Backend, writer.Tenant, site);
                authority = ChangeAuthority();
                authorityWait = await SerialNativeWait(observer, providerWait.Backend, control.ProcessID, $"cad-provider-{order}", authority);
                Assert.Equal(before, await ReadProviderWriteSet(observer, writer.Tenant, site));
                await holdReceipt.RollbackAsync();
                await pending;
                firstCommit = await ReadProviderWriteSet(observer, writer.Tenant, site);
                Assert.False(authority.IsCompleted);
                await holdTenant.RollbackAsync();
            }
            else
            {
                await using var holdAuthority = permissionFirst
                    ? await HoldUnderlayTable(control, "Sys_RoleAction")
                    : await HoldUnderlayTenant(control, writer.Tenant);
                authority = ChangeAuthority();
                authorityWait = await WaitForFence(observer, control.ProcessID,
                    permissionFirst ? "%DELETE%Sys_RoleAction%" : "%UPDATE%Sys_Tenants%", null, authority);
                pending = Replace(body, key);
                providerWait = await SerialNativeWait(observer, authorityWait.Backend, barrier.ProcessID, $"cad-provider-{order}", pending);
                siteLockHeld = await HoldsProviderSiteLock(observer, providerWait.Backend, writer.Tenant, site);
                Assert.Equal(before, await ReadProviderWriteSet(observer, writer.Tenant, site));
                await holdAuthority.RollbackAsync();
                await authority;
                firstCommit = await ReadProviderWriteSet(observer, writer.Tenant, site);
                await holdReceipt.RollbackAsync();
            }
        }

        using var changed = await authority;
        using var response = await pending;
        var after = await ReadProviderWriteSet(observer, writer.Tenant, site);
        await DeviceEvidence($"cad-provider-{order}.json", new
        {
            Tenant = writer.Tenant, Site = site, Key = key, Writer = writer.User, ProviderWait = providerWait, AuthorityWait = authorityWait,
            SiteLockHeld = siteLockHeld, ChangeStatus = (int)changed.StatusCode, ReplaceStatus = (int)response.StatusCode,
            Before = before, FirstCommit = firstCommit, After = after
        });
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));

        if (permissionFirst)
        {
            var committed = await changed.Content.ReadFromJsonAsync<IamOperationV1>();
            Assert.Equal(revoke!.OperationId, committed!.OperationId);
            Assert.Equal("COMMITTED", committed.State);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(authorityWait.Backend, providerWait.Blocker);
            Assert.False(siteLockHeld);
            Assert.Equal(before, after);
            Assert.Equal(before, firstCommit);
            var actions = await writer.Db.Sys_RoleActions.AsNoTracking().Where(action => action.RoleId == 72 && action.MenuId == spaceMenu)
                .Select(action => action.ActionCode).ToArrayAsync();
            Assert.Equal(new[] { "model:read" }, actions);
            var retained = await ReadCurrent();
            Assert.Equal(initial.ConfigurationRevision, retained.ConfigurationRevision);
            Assert.Equal(initial.Primary, retained.Primary);
            using var denied = await Replace(body, key);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(before, await ReadProviderWriteSet(observer, writer.Tenant, site));
            return;
        }

        JsonElement original = default;
        if (businessFirst)
        {
            Assert.True(siteLockHeld);
            Assert.Equal(providerWait.Backend, authorityWait.Blocker);
            original = await ReadProviderReplacement(response, replay: false);
            Assert.True(firstCommit.TenantEnabled);
            Assert.Equal(firstCommit with { TenantEnabled = false }, after);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal(authorityWait.Backend, providerWait.Blocker);
            Assert.False(siteLockHeld);
            Assert.Equal(before with { TenantEnabled = false }, firstCommit);
            Assert.Equal(firstCommit, after);
        }
        using var stale = await Replace(body, key);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, changed);
        actor = await Login(host, writer);
        using var resumed = await Replace(body, key);
        var result = await ReadProviderReplacement(resumed, replay: businessFirst);
        if (businessFirst) Assert.Equal(original.GetRawText(), result.GetRawText());
        Assert.Equal(site, result.GetProperty("siteId").GetGuid());
        Assert.Equal(2, result.GetProperty("configurationRevision").GetInt64());
        Assert.Equal(writer.User, result.GetProperty("updatedBy").GetGuid());
        Assert.False(result.GetProperty("canPrepareCad").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("primary").ValueKind);
        var current = await ReadCurrent();
        Assert.Equal(2, current.ConfigurationRevision);
        Assert.Null(current.Primary);
        Assert.Equal(writer.User, current.UpdatedBy);
        var persisted = await ReadProviderWriteSet(observer, writer.Tenant, site);
        if (businessFirst) Assert.Equal(firstCommit, persisted);
        Assert.Equal(before.Certifications, persisted.Certifications);
        using var versions = JsonDocument.Parse(persisted.Configurations);
        Assert.Collection(versions.RootElement.EnumerateArray(),
            v1 =>
            {
                Assert.Equal(history, v1.GetProperty("Id").GetGuid());
                Assert.Equal(1, v1.GetProperty("ConfigurationRevision").GetInt64());
                Assert.False(v1.GetProperty("IsCurrent").GetBoolean());
            },
            v2 =>
            {
                Assert.Equal(2, v2.GetProperty("ConfigurationRevision").GetInt64());
                Assert.True(v2.GetProperty("IsCurrent").GetBoolean());
                Assert.Equal(writer.User, v2.GetProperty("ApprovedBy").GetGuid());
            });
        using var receipts = JsonDocument.Parse(persisted.Receipts);
        Assert.Equal(writer.User, Assert.Single(receipts.RootElement.EnumerateArray()).GetProperty("PrincipalId").GetGuid());
        using var replay = await Replace(body, key);
        Assert.Equal(result.GetRawText(), (await ReadProviderReplacement(replay, replay: true)).GetRawText());
        using var changedKey = await Replace(body with { Reason = "Different intent" }, key);
        Assert.Equal(HttpStatusCode.Conflict, changedKey.StatusCode);
        using var differentKey = await Replace(body, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Conflict, differentKey.StatusCode);
        Assert.Equal(persisted, await ReadProviderWriteSet(observer, writer.Tenant, site));

        Task<HttpResponseMessage> Replace(ReplaceSpaceCadProviderConfigurationRequest input, string requestKey)
        {
            var request = OperationsRequest(actor, HttpMethod.Put, $"{path}/cad-provider-configuration", input);
            request.Headers.Add("Idempotency-Key", requestKey);
            return host.Send(request);
        }

        Task<HttpResponseMessage> ChangeAuthority() => permissionFirst
            ? host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke))
            : host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"), suspend));

        async Task<SpaceCadSiteCapabilityDto> ReadCurrent()
        {
            using var read = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{path}/cad-capability"));
            Assert.True(read.StatusCode == HttpStatusCode.OK, await OperationsFailure(read));
            return (await read.Content.ReadFromJsonAsync<SpaceCadSiteCapabilityDto>())!;
        }
    }

    private static async Task<Guid> SeedHistoricalProvider(SpaceContext space, Harness writer, Guid site)
    {
        var now = DateTime.UtcNow;
        var previous = SpaceCadSiteProviderConfiguration.Create(writer.Tenant, site, 1,
            "Simulated historical CAD provider", writer.User, now.AddDays(-2));
        space.CadProviderConfigurations.Add(previous);
        space.CadProviderCertifications.Add(SpaceCadSiteProviderCertification.Create(writer.Tenant, previous.Id, site,
            "historical.local", "1.0", SpaceCadProviderRole.Primary, SpaceCadProviderDeploymentMode.OnPremisesIsolatedWorker,
            SpaceCadProviderDataBoundary.SiteLocal, "evidence://simulated/approval", null, now.AddDays(-2), now.AddDays(7),
            supportsDwg: true, supportsDxf: true, licensingApproved: true, securityApproved: true, dataRegionApproved: true,
            deletionRetentionApproved: true, qualificationScore: 90, qualificationRubricVersion: "cad-ga-v1",
            goldenDatasetSha256: new string('d', 64), frozenEnvironmentSha256: new string('e', 64),
            qualificationEvidenceReference: "evidence://simulated/qualification"));
        await space.SaveChangesAsync();
        return previous.Id;
    }

    private static async Task<JsonElement> ReadProviderReplacement(HttpResponseMessage response, bool replay)
    {
        Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
        Assert.Equal(replay ? "true" : "false", response.Headers.GetValues("Idempotent-Replay").Single());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(replay, json.RootElement.GetProperty("idempotentReplay").GetBoolean());
        return json.RootElement.GetProperty("capability").Clone();
    }

    private static async Task<bool> HoldsProviderSiteLock(NpgsqlConnection db, int backend, Guid tenant, Guid site)
    {
        await using var query = new NpgsqlCommand("""
            SELECT EXISTS (SELECT FROM pg_locks WHERE pid=@backend AND locktype='advisory' AND mode='ExclusiveLock' AND granted
                AND objsubid=1 AND ((classid::bigint << 32) | objid::bigint)=@siteLock)
            """, db);
        query.Parameters.AddWithValue("backend", backend);
        query.Parameters.AddWithValue("siteLock", DatabaseResourceLocks.PostgreSqlKey($"space:cad-provider:{tenant:N}:{site:N}"));
        return Assert.IsType<bool>(await query.ExecuteScalarAsync());
    }

    private sealed record ProviderWriteSet(bool TenantEnabled, string Configurations, string Certifications, string Receipts);

    private static async Task<ProviderWriteSet> ReadProviderWriteSet(NpgsqlConnection db, Guid tenant, Guid site)
    {
        await using var query = new NpgsqlCommand("""
            SELECT tenant."Enable",
                (SELECT COALESCE(jsonb_agg(to_jsonb(p) ORDER BY p."ConfigurationRevision"), '[]')::text
                 FROM public."Space_CadSiteProviderConfiguration" p WHERE p."TenantId"=@tenant AND p."SiteId"=@site),
                (SELECT COALESCE(jsonb_agg(to_jsonb(c) ORDER BY c."Id"), '[]')::text
                 FROM public."Space_CadSiteProviderCertification" c WHERE c."TenantId"=@tenant AND c."SiteId"=@site),
                (SELECT COALESCE(jsonb_agg(to_jsonb(i) ORDER BY i."Id"), '[]')::text
                 FROM public."Space_IdempotencyRecord" i WHERE i."TenantId"=@tenant AND i."Operation"=@operation)
            FROM public."Sys_Tenants" tenant WHERE tenant."Id"=@tenant
            """, db);
        query.Parameters.AddWithValue("tenant", tenant);
        query.Parameters.AddWithValue("site", site);
        query.Parameters.AddWithValue("operation", $"cad-provider-config:{site:N}");
        await using var result = await query.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        return new(result.GetBoolean(0), result.GetString(1), result.GetString(2), result.GetString(3));
    }
}
