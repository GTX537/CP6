using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs.Space;
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
    [InlineData("refresh", false)]
    [InlineData("refresh", true)]
    [InlineData("bind", false)]
    [InlineData("bind", true)]
    [InlineData("bind-batch", false)]
    [InlineData("bind-batch", true)]
    [InlineData("place", false)]
    [InlineData("place", true)]
    [InlineData("codes", false)]
    [InlineData("codes", true)]
    [InlineData("template", false)]
    [InlineData("template", true)]
    [InlineData("draft-template", false)]
    [InlineData("draft-template", true)]
    public async Task PGT017_space_catalog_writes_share_the_native_commit_order(string kind, bool pauseFirst)
    {
        await using var writer = await Seed();
        await using var manager = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:edit", "model:read", "integration:manage");
        var connection = writer.Db.Database.GetConnectionString()!;
        var site = Guid.NewGuid();
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, lease) = await SeedSpaceDesignDraft(space, execution, site);
        var geometry = await SeedCatalogGeometry(writer, space, version, floor, site, kind);

        var settings = IamUserAssignmentsPostgreSqlTests.IdentityEnvironment(writer.Tenant);
        foreach (var pair in SpaceDesignSettings(writer.Tenant, site))
            settings[pair.Key] = pair.Value;
        await using var host = await AuthenticationApiProcess.Start(connection, settings);
        var actor = await Login(host, writer);
        var administrator = await Login(host, manager);
        var write = await SpaceCatalogCommand(host, actor, kind, version, floor, lease, geometry);
        using var tenant = await host.Send(OperationsRequest(administrator, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(tenant);
        var suspend = new TenantManagementCommandV1("space-catalog-pause", active.Revision, "suspend", null);
        var authority = await ReadPauseAuthority(writer, manager.User);
        var original = await ReadCatalogEffects(space, writer);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();

        var race = await ObserveAssetRace(connection, observer, write.Table, write.Verb, "Sys_Tenants", "UPDATE", pauseFirst,
            Send, () => host.Send(OperationsRequest(administrator, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"), suspend)),
            async () =>
            {
                Assert.Equal(original, await ReadCatalogEffects(space, writer));
                Assert.Equal(authority, await ReadPauseAuthority(writer, manager.User));
            });
        using var result = race.Business;
        using var paused = race.Authority;
        var receipt = await NumberHttpData<TenantManagementReceiptV1>(paused);
        var stoppedAuthority = await ReadPauseAuthority(writer, manager.User);
        CheckPause(authority, stoppedAuthority, receipt, suspend);
        var afterRace = await ReadCatalogEffects(space, writer);
        await DeviceEvidence($"space-catalog-{kind}-{pauseFirst}.json", new
        {
            kind, pauseFirst, race.FirstWait, race.RootWait, BeforeAuthority = authority, AfterAuthority = stoppedAuthority,
            Before = original, After = afterRace, Status = (int)result.StatusCode
        });

        if (pauseFirst)
        {
            Assert.True(result.StatusCode == HttpStatusCode.Unauthorized, await OperationsFailure(result));
            Assert.Equal(original, afterRace);
        }
        else
        {
            Assert.NotEqual(original, afterRace);
            await AssertCatalogCommit(kind, result, write.Status, space, version, geometry);
        }
        using var stale = await Send();
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);
        Assert.Equal(afterRace, await ReadCatalogEffects(space, writer));

        if (!pauseFirst && kind is not ("codes" or "template" or "draft-template"))
            return;
        await ResumeAssetTenant(host, administrator, writer.Tenant, receipt);
        actor = await Login(host, writer);
        if (pauseFirst)
        {
            using var restored = await Send();
            await AssertCatalogCommit(kind, restored, write.Status, space, version, geometry);
            Assert.NotEqual(afterRace, await ReadCatalogEffects(space, writer));
        }
        if (kind is "codes" or "template" or "draft-template")
        {
            var committed = await ReadCatalogEffects(space, writer);
            using var replay = await Send();
            await AssertCatalogCommit(kind, replay, write.Status, space, version, geometry, expectedReplay: true);
            Assert.Equal(committed, await ReadCatalogEffects(space, writer));
        }

        Task<HttpResponseMessage> Send()
        {
            var request = OperationsRequest(actor, HttpMethod.Post, write.Path, write.Body);
            if (write.Key is not null)
                request.Headers.Add("Idempotency-Key", write.Key);
            return host.Send(request);
        }
    }


    [CurrentPermissionPostgresFact]
    public async Task PGT017_space_catalog_refresh_rechecks_the_current_integration_action()
    {
        await using var writer = await Seed();
        var menu = await GrantSpaceDesignActions(writer, "model:edit", "integration:manage");
        var site = Guid.NewGuid();
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var connection = writer.Db.Database.GetConnectionString()!;
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var (version, floor, _) = await SeedSpaceDesignDraft(space, execution, site);
        await SeedCatalogGeometry(writer, space, version, floor, site, "refresh");
        await using var host = await AuthenticationApiProcess.Start(connection, SpaceDesignSettings(writer.Tenant, site));
        var actor = await Login(host, writer);
        var before = await ReadCatalogEffects(space, writer);

        await using var control = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> writing;
        FenceWait wait;
        await using (var change = await control.BeginTransactionAsync())
        {
            await using var revoke = new NpgsqlCommand("""
                SELECT cp6_authentication_v1.acquire();
                DELETE FROM public."Sys_RoleAction"
                WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='integration:manage';
                """, control, change);
            revoke.Parameters.AddWithValue("tenant", writer.Tenant);
            revoke.Parameters.AddWithValue("menu", menu);
            Assert.Equal(1, await revoke.ExecuteNonQueryAsync());
            writing = host.Send(OperationsRequest(actor, HttpMethod.Post,
                $"{SpaceDesignPath}/versions/{version.Id:D}/wms-adoption/refresh"));
            wait = await WaitForFence(observer, control.ProcessID, "%cp6_authentication_v1.acquire()%", null, writing);
            await change.CommitAsync();
        }

        using var denied = await writing;
        Assert.True(denied.StatusCode == HttpStatusCode.Forbidden, await OperationsFailure(denied));
        var after = await ReadCatalogEffects(space, writer);
        Assert.Equal(before, after);
        await DeviceEvidence("space-catalog-refresh-integration-revoked.json", new { Wait = wait, Before = before, After = after, Status = (int)denied.StatusCode });
    }

    private static async Task<CatalogGeometry> SeedCatalogGeometry(Harness writer, SpaceContext space,
        SpaceModelVersion version, SpaceFloorRevision floor, Guid site, string kind)
    {
        if (kind == "draft-template")
        {
            space.Attach(floor);
            floor.ConfigureBoundary("""{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""", "LOCAL_MM_Z_UP");
            await space.SaveChangesAsync();
        }
        if (kind == "template")
            return new(Guid.Empty, [], []);

        writer.Db.Space_Sites.Add(new Space_Site
        {
            Id = site, SiteCode = "W654", SiteName = "Simulated catalog site", WarehouseCd = "W654",
        });
        var binCount = kind is "codes" or "draft-template" ? 0 : kind == "bind-batch" ? 2 : 1;
        var wmsIds = Enumerable.Range(1, binCount).Select(i =>
        {
            var id = Guid.NewGuid();
            writer.Db.WmsBins.Add(new WmsBin { Id = id, WarehouseCd = "W654", LocationCode = $"W654-{i:00}", Version = 17 });
            return id;
        }).ToArray();
        if (kind == "codes")
        {
            writer.Db.Space_CodeRules.Add(new Space_CodeRule
            {
                RuleName = "W654 actual code rule",
                IsDefault = true,
                Segments = JsonSerializer.Serialize<CodeSegmentDef[]>(
                    [new() { Key = "zone", Source = "zone-code", Sep = "-" },
                     new() { Key = "column", Source = "col", Width = 2, Sep = "" }]),
            });
        }
        await writer.Db.SaveChangesAsync();

        var zone = SpaceZoneRevision.Create(writer.Tenant, version.Id, Guid.NewGuid(), floor.LogicalId, "Z1", 1);
        zone.ConfigureShape("""{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""");
        Guid? aisleId = null;
        if (kind == "draft-template")
        {
            var aisle = SpaceAisleRevision.Create(writer.Tenant, version.Id, Guid.NewGuid(), zone.LogicalId, "A1", 1);
            aisle.ConfigureShape(
                """{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""",
                """{"schemaVersion":1,"points":[[0,4000],[10000,4000]]}""");
            space.AisleRevisions.Add(aisle);
            aisleId = aisle.LogicalId;
        }
        var rack = SpaceRackRevision.Create(writer.Tenant, version.Id, Guid.NewGuid(), floor.LogicalId, zone.LogicalId,
            "R1", aisleId);
        rack.ConfigureGeometry(1000, 1000, 0, 0, 4000, 1100, aisleId.HasValue ? 1300 : 3400);
        var level = SpaceRackLevelRevision.Create(writer.Tenant, version.Id, Guid.NewGuid(), rack.LogicalId,
            1, 0, 1200, 4, 1, 1000, 1100, 900, beamHeight: aisleId.HasValue ? 100 : 0);
        space.AddRange(zone, rack, level);
        var locationCount = kind is "place" or "refresh" or "draft-template" ? 0 : kind == "bind-batch" ? 2 : 1;
        var locations = Enumerable.Range(1, locationCount).Select(i =>
            SpaceLocationRevision.Create(writer.Tenant, version.Id, Guid.NewGuid(), floor.LogicalId, rack.LogicalId,
                kind == "codes" ? null : $"OLD-{i:00}", i, 1, 1, 1000, 1200, 1100)).ToArray();
        space.AddRange(locations);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return new(rack.LogicalId, locations.Select(x => x.LogicalId).ToArray(), wmsIds);
    }

    private static async Task<CatalogWrite> SpaceCatalogCommand(AuthenticationApiProcess host, Dictionary<string, string> actor, string kind,
        SpaceModelVersion version, SpaceFloorRevision floor, SpaceEditLease lease, CatalogGeometry geometry)
    {
        var versionPath = $"{SpaceDesignPath}/versions/{version.Id:D}";
        var wms = versionPath + "/wms-adoption";
        const string templateCode = "W654-TENANT";
        if (kind == "refresh")
            return new(wms + "/refresh", null, "Space_WmsAdoption", "INSERT", HttpStatusCode.OK);
        if (kind == "codes")
        {
            var path = $"{versionPath}/floors/{floor.LogicalId:D}/location-codes";
            var input = new PreviewSpaceLocationCodesRequest(SpaceDesignCodingContract.SchemaVersion,
                SpaceDesignCodingContract.FillEmpty, null, floor.Revision, version.ContentRevision);
            using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, path + ":preview", input));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            var preview = (await response.Content.ReadFromJsonAsync<PreviewSpaceLocationCodesResponse>())!;
            Assert.Equal(1, preview.ChangedCount);
            return new(path + ":apply", new ApplySpaceLocationCodesRequest(SpaceDesignCodingContract.SchemaVersion, Guid.NewGuid(),
                lease.ClientInstanceId, lease.LeaseId, preview.Mode, preview.ScopeZoneLogicalId, preview.BaseFloorRevision,
                preview.BaseContentRevision, preview.ProposalHash), "Space_ElementCommandBatch", "INSERT", HttpStatusCode.OK);
        }
        if (kind == "draft-template")
        {
            using var response = await host.Send(OperationsRequest(actor, HttpMethod.Post, versionPath + "/tenant-template-preview",
                new PreviewTenantSpaceWarehouseTemplateFromDraftRequest(version.ContentRevision)));
            Assert.True(response.StatusCode == HttpStatusCode.OK, await OperationsFailure(response));
            var preview = (await response.Content.ReadFromJsonAsync<SpaceDraftWarehouseTemplatePreviewDto>())!;
            Assert.False(preview.WritesTemplate);
            return new(versionPath + "/tenant-templates", new CreateTenantSpaceWarehouseTemplateFromDraftRequest(
                templateCode, "Draft catalog fixture", null, version.ContentRevision, preview.ProposalHash),
                "Space_WarehouseTemplate", "INSERT", HttpStatusCode.Created, Guid.NewGuid().ToString("N"));
        }
        if (kind == "template")
        {
            var input = new CreateTenantSpaceWarehouseTemplateRequest(templateCode, "Direct catalog fixture", null,
                SpaceWarehouseTemplateContract.SchemaVersion,
                [new("floor", "F1", "Ground", 1, 0, 10000, 8000, 6000)],
                [new("zone", "floor", "STORAGE", "Storage", 0, 0, 10000, 8000)],
                [new("aisle", "floor", "zone", "A01", 0, 4000, 10000, 4000)],
                [new("rack", "floor", "zone", "aisle", "R01", 1000, 1000, 0, 0, 2000, 1000, 3000, 2, 3, 1)]);
            return new(SpaceDesignPath + "/templates", input, "Space_WarehouseTemplate", "INSERT", HttpStatusCode.Created,
                Guid.NewGuid().ToString("N"));
        }

        using (var refreshed = await host.Send(OperationsRequest(actor, HttpMethod.Post, wms + "/refresh")))
            Assert.True(refreshed.StatusCode == HttpStatusCode.OK, await OperationsFailure(refreshed));
        using var list = await host.Send(OperationsRequest(actor, HttpMethod.Get, wms + "/locations"));
        Assert.True(list.StatusCode == HttpStatusCode.OK, await OperationsFailure(list));
        var adoptions = (await list.Content.ReadFromJsonAsync<SpacePage<SpaceWmsAdoptionDto>>())!.Items
            .OrderBy(item => item.WmsLocationCode).ToArray();
        Assert.Equal(geometry.WmsIds, adoptions.Select(item => item.WmsLogicalId).ToArray());
        return kind switch
        {
            "bind" => new($"{wms}/locations/{adoptions[0].Id:D}/bind",
                new BindSpaceWmsAdoptionRequest(geometry.Locations[0], adoptions[0].RowVersion),
                "Space_WmsAdoption", "UPDATE", HttpStatusCode.OK),
            "bind-batch" => new(wms + "/bindings:batch", new BatchBindSpaceWmsAdoptionRequest(adoptions
                .Select((item, index) => new BatchBindSpaceWmsAdoptionItem(item.Id, geometry.Locations[index], item.RowVersion)).ToArray()),
                "Space_WmsAdoption", "UPDATE", HttpStatusCode.OK),
            "place" => new($"{wms}/locations/{adoptions[0].Id:D}/place",
                new PlaceSpaceWmsAdoptionRequest(floor.LogicalId, geometry.Rack, 2, 1, 1, adoptions[0].RowVersion),
                "Space_WmsAdoption", "UPDATE", HttpStatusCode.OK),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static async Task AssertCatalogCommit(string kind, HttpResponseMessage response, HttpStatusCode status,
        SpaceContext space, SpaceModelVersion version, CatalogGeometry geometry, bool expectedReplay = false)
    {
        Assert.True(response.StatusCode == status, await OperationsFailure(response));
        if (kind is "template" or "draft-template")
        {
            var receipt = (await response.Content.ReadFromJsonAsync<CreateTenantSpaceWarehouseTemplateResponse>())!;
            Assert.Equal(expectedReplay, receipt.IdempotentReplay);
            var template = await space.WarehouseTemplates.AsNoTracking().SingleAsync(x => x.Id == receipt.Template.Id);
            var stored = await space.WarehouseTemplateVersions.AsNoTracking().SingleAsync(x => x.TemplateId == template.Id);
            Assert.Equal("W654-TENANT", template.TemplateCode);
            Assert.Equal(receipt.Template.LatestVersion.Id, stored.Id);
            Assert.Equal(receipt.Template.LatestVersion.ContentHash, stored.ContentHash);
            Assert.Equal(1, await space.IdempotencyRecords.CountAsync());
            return;
        }
        var current = await space.Versions.AsNoTracking().SingleAsync(x => x.Id == version.Id);
        if (kind == "codes")
        {
            var applied = (await response.Content.ReadFromJsonAsync<ApplySpaceLocationCodesResponse>())!;
            Assert.Equal(expectedReplay, applied.IdempotentReplay);
            Assert.Equal(1, applied.AppliedCount);
            Assert.Equal(1, current.ContentRevision);
            Assert.Equal("Z1-01", await space.LocationRevisions.AsNoTracking().Where(x => x.ModelVersionId == version.Id)
                .Select(x => x.LocationCode).SingleAsync());
            Assert.Equal(1, await space.ElementCommandBatches.CountAsync());
            Assert.Equal(1, await space.ElementCommandRecords.CountAsync());
            return;
        }
        var adoptions = await space.WmsAdoptions.AsNoTracking().OrderBy(x => x.WmsLocationCode).ToArrayAsync();
        Assert.Equal(geometry.WmsIds, adoptions.Select(x => x.WmsLogicalId).ToArray());
        Assert.All(adoptions, x => Assert.Equal(Cp6SpaceWmsAdapter.AdapterId, x.AdapterId));
        if (kind == "refresh")
        {
            Assert.Equal(0, current.ContentRevision);
            Assert.Equal(SpaceWmsAdoptionStatus.Unbound, Assert.Single(adoptions).Status);
            var issue = await space.Issues.AsNoTracking().SingleAsync(x => x.ModelVersionId == version.Id);
            Assert.Equal(SpaceErrorCodes.WmsLocationUnbound, issue.Code);
            return;
        }
        Assert.Equal(1, current.ContentRevision);
        var locations = await space.LocationRevisions.AsNoTracking().Where(x => x.ModelVersionId == version.Id).OrderBy(x => x.LocationCode).ToArrayAsync();
        Assert.Equal(adoptions.Length, locations.Length);
        for (var i = 0; i < adoptions.Length; i++)
        {
            Assert.Equal(SpaceWmsAdoptionStatus.Bound, adoptions[i].Status);
            Assert.Equal(locations[i].LogicalId, adoptions[i].LocationLogicalId);
            Assert.Equal(locations[i].LocationCode, adoptions[i].WmsLocationCode);
            Assert.Equal(SpaceExternalBindingState.Bound, locations[i].ExternalBindingState);
        }
        Assert.Empty(await space.Issues.AsNoTracking().Where(x => x.ModelVersionId == version.Id && x.Status == SpaceIssueStatus.Open).ToArrayAsync());
        if (kind == "place")
        {
            var placed = Assert.Single(locations);
            Assert.Equal(Assert.Single(geometry.WmsIds), placed.LogicalId);
            Assert.Equal((2, 1000, 1200, 1100, 900m), (placed.ColumnNo, placed.Width, placed.Height, placed.Depth, placed.MaxLoad!.Value));
        }
    }

    private static async Task<string> ReadCatalogEffects(SpaceContext space, Harness writer) => JsonSerializer.Serialize(new
    {
        Versions = await space.Versions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Floors = await space.FloorRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Locations = await space.LocationRevisions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Adoptions = await space.WmsAdoptions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Issues = await space.Issues.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Batches = await space.ElementCommandBatches.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Entries = await space.ElementCommandRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Templates = await space.WarehouseTemplates.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        TemplateVersions = await space.WarehouseTemplateVersions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Receipts = await space.IdempotencyRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Wms = await writer.Db.WmsBins.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
    });

    private sealed record CatalogGeometry(Guid Rack, Guid[] Locations, Guid[] WmsIds);
    private sealed record CatalogWrite(string Path, object? Body, string Table, string Verb, HttpStatusCode Status, string? Key = null);
}
