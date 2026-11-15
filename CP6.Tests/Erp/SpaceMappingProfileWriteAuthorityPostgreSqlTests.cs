using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using CP6.Tests.Platform;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("cad", false, "pause-first")]
    [InlineData("cad", false, "business-first")]
    [InlineData("cad", true, "pause-first")]
    [InlineData("cad", true, "business-first")]
    [InlineData("cad", true, "permission-first")]
    [InlineData("excel", false, "pause-first")]
    [InlineData("excel", false, "business-first")]
    [InlineData("excel", true, "pause-first")]
    [InlineData("excel", true, "business-first")]
    [InlineData("excel", true, "permission-first")]
    public async Task PGT017_space_mapping_profile_save_and_authority_share_one_transaction(string kind, bool update, string order)
    {
        var businessFirst = order == "business-first";
        var permissionFirst = order == "permission-first";
        await using var writer = await Seed();
        var spaceMenu = await GrantSpaceDesignActions(writer, "model:read", "model:edit");
        await using var administrator = permissionFirst ? null : await TenantManagementSeed();
        if (permissionFirst)
        {
            var roleMenus = await writer.Db.Sys_RoleActions.AsNoTracking().Where(action => action.RoleId == 72)
                .Select(action => action.MenuId).Distinct().ToArrayAsync();
            foreach (var menuId in roleMenus)
                writer.Db.Sys_RoleMenus.Add(new() { RoleId = 72, MenuId = menuId });
            await writer.Db.SaveChangesAsync();
            writer.Db.ChangeTracker.Clear();
            await SeedFenceAdministrator(writer);
        }

        var connection = writer.Db.Database.GetConnectionString()!;
        await using var space = SpaceDesignContext(connection, new SpaceDesignExecution(writer.Tenant, writer.User));
        await space.Database.MigrateAsync();
        var settings = SpaceDesignSettings(writer.Tenant, Guid.NewGuid());
        if (permissionFirst)
        {
            foreach (var pair in IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(writer.Tenant))
                settings[pair.Key] = pair.Value;
        }
        if (int.TryParse(Environment.GetEnvironmentVariable("CP6_SPACE_MAPPING_PROFILE_HTTP_PORT"), out var port))
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var manager = permissionFirst
            ? await Login(host, "fence-manager", "Fence1!Password", writer.Tenant.ToString("N"))
            : await Login(host, administrator!);

        var path = $"{SpaceDesignPath}/mapping-profiles/{kind}";
        var table = kind == "cad" ? "Space_LayerMappingProfile" : "Space_ExcelMappingProfile";
        var systemId = kind == "cad" ? StandardSpaceCadMappingProfileCatalog.SystemProfile.ProfileId : SpaceExcelMappingService.SystemStandardProfileId;
        var name = $"Simulated {kind} profile {Guid.NewGuid():N}";
        var createKey = Guid.NewGuid().ToString("N");
        using var systemResponse = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{path}/{systemId:D}"));
        Assert.Equal(HttpStatusCode.OK, systemResponse.StatusCode);
        object create;
        if (kind == "cad")
        {
            var system = (await systemResponse.Content.ReadFromJsonAsync<SpaceCadMappingProfileDto>())!;
            create = new SaveSpaceCadMappingProfileRequest(null, name, true, system.Rules,
                CopyFromProfileId: system.Id, CopyFromVersion: system.Version);
        }
        else
        {
            var system = (await systemResponse.Content.ReadFromJsonAsync<SpaceExcelMappingProfileDto>())!;
            create = new SaveSpaceExcelMappingProfileRequest(null, name, system.Definition,
                CopyFromProfileId: system.Id, CopyFromVersion: system.Version);
        }

        var key = createKey;
        var body = create;
        JsonElement original = default;
        if (update)
        {
            using var created = await Save(create, createKey);
            original = await ReadMappingProfileResult(created, HttpStatusCode.Created, replay: false);
            var id = original.GetProperty("id").GetGuid();
            var rowVersion = original.GetProperty("rowVersion").GetString();
            Assert.Equal(1, original.GetProperty("version").GetInt32());
            Assert.Equal(writer.User, original.GetProperty("createdBy").GetGuid());
            Assert.False(string.IsNullOrWhiteSpace(rowVersion));
            name += " updated";
            key = Guid.NewGuid().ToString("N");
            body = create switch
            {
                SaveSpaceCadMappingProfileRequest cad => new SaveSpaceCadMappingProfileRequest(id, name, true, cad.Rules, rowVersion),
                SaveSpaceExcelMappingProfileRequest excel => new SaveSpaceExcelMappingProfileRequest(id, name, excel.Definition, rowVersion),
                _ => throw new InvalidOperationException("Unsupported mapping profile request.")
            };
        }

        IamRolePolicyCommandV1? revoke = null;
        TenantManagementCommandV1? suspend = null;
        if (permissionFirst)
            revoke = await RevokeActions(host, manager, "model:edit");
        else
        {
            using var snapshot = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
            var active = await NumberHttpData<TenantManagementSnapshotV1>(snapshot);
            suspend = new(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null);
        }

        await using var authorityDb = new NpgsqlConnection(connection);
        await using var businessDb = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await authorityDb.OpenAsync();
        await businessDb.OpenAsync();
        await observer.OpenAsync();
        var before = await ReadMappingProfileWrites(observer, writer.Tenant, table);
        Task<HttpResponseMessage> request;
        Task<HttpResponseMessage> authorityChange;
        FenceWait businessWait, authorityWait;
        MappingProfileWriteSet firstCommit;
        if (businessFirst)
        {
            // Stop at the second SaveChanges, after the profile and version are staged in the same transaction.
            await using var holdReceipt = await HoldUnderlayTable(businessDb, "Space_IdempotencyRecord");
            await using var holdTenant = await HoldUnderlayTenant(authorityDb, writer.Tenant);
            request = Save(body, key);
            businessWait = await WaitForFence(observer, businessDb.ProcessID, "%INSERT%Space_IdempotencyRecord%", null, request);
            authorityChange = ChangeAuthority();
            authorityWait = await WaitForFence(observer, businessWait.Backend, "%cp6_authentication_v1.acquire()%", null, authorityChange);
            Assert.Equal(before, await ReadMappingProfileWrites(observer, writer.Tenant, table));
            await holdReceipt.RollbackAsync();
            await request;
            firstCommit = await ReadMappingProfileWrites(observer, writer.Tenant, table);
            Assert.False(authorityChange.IsCompleted);
            await holdTenant.RollbackAsync();
        }
        else
        {
            await using var holdChange = permissionFirst
                ? await HoldUnderlayTable(authorityDb, "Sys_RoleAction")
                : await HoldUnderlayTenant(authorityDb, writer.Tenant);
            authorityChange = ChangeAuthority();
            authorityWait = await WaitForFence(observer, authorityDb.ProcessID,
                permissionFirst ? "%DELETE%Sys_RoleAction%" : "%UPDATE%Sys_Tenants%", null, authorityChange);
            request = Save(body, key);
            businessWait = await WaitForFence(observer, authorityWait.Backend, "%cp6_authentication_v1.acquire()%", null, request);
            Assert.Equal(before, await ReadMappingProfileWrites(observer, writer.Tenant, table));
            await holdChange.RollbackAsync();
            await authorityChange;
            firstCommit = await ReadMappingProfileWrites(observer, writer.Tenant, table);
        }

        using var changed = await authorityChange;
        using var response = await request;
        var after = await ReadMappingProfileWrites(observer, writer.Tenant, table);
        await DeviceEvidence($"space-mapping-profile-{kind}-{(update ? "update" : "create")}-{order}.json", new
        {
            Path = path, TenantId = writer.Tenant, ActorId = writer.User, Key = key, BusinessWait = businessWait,
            AuthorityWait = authorityWait, AuthorityStatus = (int)changed.StatusCode, RequestStatus = (int)response.StatusCode,
            Before = before, FirstCommit = firstCommit, After = after
        });
        Assert.True(changed.StatusCode == HttpStatusCode.OK, await OperationsFailure(changed));

        if (permissionFirst)
        {
            var committed = await changed.Content.ReadFromJsonAsync<IamOperationV1>();
            Assert.Equal(revoke!.OperationId, committed!.OperationId);
            Assert.Equal("COMMITTED", committed.State);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(response));
            Assert.Equal(before, firstCommit);
            Assert.Equal(before, after);
            var actions = await writer.Db.Sys_RoleActions.AsNoTracking()
                .Where(action => action.RoleId == 72 && action.MenuId == spaceMenu).Select(action => action.ActionCode).ToArrayAsync();
            Assert.Equal(["model:read"], actions);
            var originalId = original.GetProperty("id").GetGuid();
            using var read = await host.Send(OperationsRequest(actor, HttpMethod.Get, $"{path}/{originalId:D}"));
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var profile = await read.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(originalId, profile.GetProperty("id").GetGuid());
            Assert.Equal(1, profile.GetProperty("version").GetInt32());
            Assert.Equal(writer.User, profile.GetProperty("createdBy").GetGuid());
            Assert.Equal(original.GetProperty("rowVersion").GetString(), profile.GetProperty("rowVersion").GetString());
            using var denied = await Save(body, key);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(before, await ReadMappingProfileWrites(observer, writer.Tenant, table));
            return;
        }

        var expectedStatus = update ? HttpStatusCode.OK : HttpStatusCode.Created;
        JsonElement committedProfile = default;
        if (businessFirst)
        {
            committedProfile = await ReadMappingProfileResult(response, expectedStatus, replay: false);
            Assert.True(firstCommit.TenantEnabled);
            Assert.Equal(firstCommit with { TenantEnabled = false }, after);
        }
        else
        {
            Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(response));
            Assert.Equal(before with { TenantEnabled = false }, firstCommit);
            Assert.Equal(firstCommit, after);
        }

        using var stale = await Save(body, key);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, changed);
        actor = await Login(host, writer);
        using var resumed = await Save(body, key);
        var result = await ReadMappingProfileResult(resumed, expectedStatus, replay: businessFirst);
        if (businessFirst)
            Assert.Equal(committedProfile.GetRawText(), result.GetRawText());
        var persisted = await ReadMappingProfileWrites(observer, writer.Tenant, table);
        if (businessFirst)
            Assert.Equal(firstCommit, persisted);

        var profileId = result.GetProperty("id").GetGuid();
        Assert.Equal(update ? 2 : 1, result.GetProperty("version").GetInt32());
        Assert.Equal(name, result.GetProperty("name").GetString());
        Assert.Equal(writer.User, result.GetProperty("createdBy").GetGuid());
        Assert.Equal(update && kind == "excel" ? profileId : systemId, result.GetProperty("basedOnProfileId").GetGuid());
        Assert.Equal(1, result.GetProperty("basedOnVersion").GetInt32());
        Assert.Equal(update ? 0 : 1, persisted.HeaderCount - before.HeaderCount);
        Assert.Equal(1, persisted.VersionCount - before.VersionCount);
        Assert.Equal(1, persisted.ReceiptCount - before.ReceiptCount);
        using var receipts = JsonDocument.Parse(persisted.Receipts);
        Assert.All(receipts.RootElement.EnumerateArray(), receipt => Assert.Equal(writer.User, receipt.GetProperty("PrincipalId").GetGuid()));
        if (update)
        {
            Assert.Equal(original.GetProperty("id").GetGuid(), profileId);
            Assert.NotEqual(original.GetProperty("rowVersion").GetString(), result.GetProperty("rowVersion").GetString());
            using var versions = JsonDocument.Parse(persisted.Versions);
            var retained = Assert.Single(versions.RootElement.EnumerateArray(), row => row.GetProperty("Version").GetInt32() == 1);
            using var oldVersions = JsonDocument.Parse(before.Versions);
            Assert.Equal(Assert.Single(oldVersions.RootElement.EnumerateArray()).GetRawText(), retained.GetRawText());
            using var originalReplay = await Save(create, createKey);
            var oldResult = await ReadMappingProfileResult(originalReplay, HttpStatusCode.Created, replay: true);
            Assert.Equal(original.GetRawText(), oldResult.GetRawText());
        }

        using var replay = await Save(body, key);
        Assert.Equal(result.GetRawText(), (await ReadMappingProfileResult(replay, expectedStatus, replay: true)).GetRawText());
        Assert.Equal(persisted, await ReadMappingProfileWrites(observer, writer.Tenant, table));

        Task<HttpResponseMessage> Save(object input, string requestKey)
        {
            var operation = OperationsRequest(actor, HttpMethod.Post, path, input);
            operation.Headers.Add("Idempotency-Key", requestKey);
            return host.Send(operation);
        }

        Task<HttpResponseMessage> ChangeAuthority() => permissionFirst
            ? host.Send(OperationsRequest(manager, HttpMethod.Put, PolicyWritePath, revoke))
            : host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"), suspend));
    }

    private static async Task<JsonElement> ReadMappingProfileResult(HttpResponseMessage response, HttpStatusCode status, bool replay)
    {
        Assert.True(response.StatusCode == status, await OperationsFailure(response));
        Assert.Equal(replay ? "true" : "false", response.Headers.GetValues("Idempotent-Replay").Single());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status == HttpStatusCode.Created, json.RootElement.GetProperty("created").GetBoolean());
        return json.RootElement.GetProperty("profile").Clone();
    }

    private sealed record MappingProfileWriteSet(bool TenantEnabled, int HeaderCount, int VersionCount, int ReceiptCount,
        string Headers, string Versions, string Receipts);

    private static async Task<MappingProfileWriteSet> ReadMappingProfileWrites(NpgsqlConnection db, Guid tenant, string table)
    {
        await using var query = new NpgsqlCommand($"""
            SELECT tenant."Enable", header.count, version.count, receipt.count, header.rows, version.rows, receipt.rows
            FROM public."Sys_Tenants" tenant
            CROSS JOIN (SELECT count(*)::int AS count, COALESCE(jsonb_agg(to_jsonb(item) ORDER BY item."Id"), '[]'::jsonb)::text AS rows
                        FROM public."{table}" item WHERE item."TenantId"=@tenant) header
            CROSS JOIN (SELECT count(*)::int AS count, COALESCE(jsonb_agg(to_jsonb(item) ORDER BY item."Id"), '[]'::jsonb)::text AS rows
                        FROM public."{table}Version" item WHERE item."TenantId"=@tenant) version
            CROSS JOIN (SELECT count(*)::int AS count, COALESCE(jsonb_agg(to_jsonb(item) ORDER BY item."Id"), '[]'::jsonb)::text AS rows
                        FROM public."Space_IdempotencyRecord" item WHERE item."TenantId"=@tenant) receipt
            WHERE tenant."Id"=@tenant
            """, db);
        query.Parameters.AddWithValue("tenant", tenant);
        await using var result = await query.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync());
        return new(result.GetBoolean(0), result.GetInt32(1), result.GetInt32(2), result.GetInt32(3),
            result.GetString(4), result.GetString(5), result.GetString(6));
    }
}
