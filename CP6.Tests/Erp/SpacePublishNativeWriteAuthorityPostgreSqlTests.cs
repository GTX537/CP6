using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Core.Services.Platform;
using CP6.Entity.DomainModels.Space;
using CP6.Space.Application;
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
    [InlineData("wms", true)]
    [InlineData("wms", false)]
    [InlineData("runtime", true)]
    [InlineData("runtime", false)]
    public async Task PGT017_space_publish_native_effect_and_pause_share_main_authority(string boundary, bool effectFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:publish", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedBackendVersions(writer, space, site);
        var relayAtRuntime = (boundary == "wms" && effectFirst) || (boundary == "runtime" && !effectFirst);
        var fixtureRoot = NewPublishFixtureRoot();
        await using var host = await StartPublishWorkerHost(connection, writer.Tenant, site, fixtureRoot,
            relayAtRuntime ? "runtime-entry" : null);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var submission = await PreparePublishSubmission(writer, space, execution, site, host, actor, versions.Releases, "publish");
        using var queued = await SendPublishSubmission(host, actor, submission);
        var ids = await CheckPublishSubmission(space, writer, versions.Releases, submission, "publish", queued, replay: false);
        var initial = await BackendWriteSet(writer, space, ids);
        Assert.Equal("Prior published floor", Assert.Single(initial.Floors).FloorName);
        Assert.Empty(initial.WmsOperations);
        using var activeResponse = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(activeResponse);
        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        BackendSnapshot whileHeld;
        FenceWait ownerWait;
        FenceWait pauseWait;
        Task<SpacePublishWorkerFixture.WorkerResult> work;
        HttpResponseMessage paused;
        if (effectFirst)
        {
            var table = boundary == "wms" ? "T_WmsBin" : "Space_Location";
            await using var hold = await HoldUnderlayTable(business, table);
            work = RunPublishWorkerAsync(fixtureRoot, 1);
            ownerWait = await WaitForFence(observer, business.ProcessID, $"%INSERT%{table}%", null, work);
            whileHeld = await BackendWriteSet(writer, space, ids);
            var pause = Pause();
            pauseWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, pause);
            await hold.RollbackAsync();
            if (relayAtRuntime)
                await SpaceArtifactWorkerFixture.ReadAsync<bool>(fixtureRoot, 1, "native-ready");
            paused = await pause;
            if (relayAtRuntime)
                SpaceArtifactWorkerFixture.Write(fixtureRoot, 1, "native-continue", true);
        }
        else
        {
            await using var hold = await HoldUnderlayTenant(control, writer.Tenant);
            var admitted = relayAtRuntime ? RunPublishWorkerAsync(fixtureRoot, 1) : null;
            if (relayAtRuntime)
                await SpaceArtifactWorkerFixture.ReadAsync<bool>(fixtureRoot, 1, "native-ready");

            var pause = Pause();
            pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
            if (relayAtRuntime)
                SpaceArtifactWorkerFixture.Write(fixtureRoot, 1, "native-continue", true);
            work = admitted ?? RunPublishWorkerAsync(fixtureRoot, 1);
            ownerWait = await WaitForFence(observer, pauseWait.Backend, "%cp6_authentication_v1.acquire%", null, work);
            whileHeld = await BackendWriteSet(writer, space, ids);
            await hold.RollbackAsync();
            paused = await pause;
        }

        using var signedPause = paused;
        Assert.Equal(HttpStatusCode.OK, signedPause.StatusCode);
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(signedPause)).Snapshot.Enable);
        Assert.Equal(SpaceJobStatus.Running, whileHeld.Job.Status);
        Assert.Equal(whileHeld.Job.ActiveAttemptId, Assert.Single(whileHeld.JobAttempts).Id);
        Assert.Equal(initial.Model.CurrentPublishedVersionId, whileHeld.Model.CurrentPublishedVersionId);

        var first = await work;
        Assert.Null(first.Error);
        Assert.Equal(1, first.Processed);
        var after = await BackendWriteSet(writer, space, ids);
        await DeviceEvidence($"space-publish-native-{boundary}-{(effectFirst ? "effect-first" : "pause-first")}.json",
            new { OwnerWait = ownerWait, PauseWait = pauseWait, WhileHeld = whileHeld, After = after });
        var finished = boundary == "runtime" && effectFirst;
        if (finished)
        {
            AssertNativeProjection(after, versions);
            Assert.Equal(SpaceJobStatus.Succeeded, after.Job.Status);
        }
        else
        {
            Assert.Equal(SpaceJobStatus.Queued, after.Job.Status);
            Assert.Equal(SpacePublishAttemptStatus.WaitingRetry, after.Attempt.Status);
            Assert.Equal("SPACE_PUBLISH_TENANT_INACTIVE", after.Attempt.LastErrorCode);
            Assert.Equal(boundary == "wms" && !effectFirst ? SpacePublishStep.ApplyWms : SpacePublishStep.ActivateRuntime,
                after.Attempt.CurrentStep);
            Assert.Equal(BackendEffectState(initial, "runtime"), BackendEffectState(after, "runtime"));
        }

        if (boundary == "wms" && !effectFirst)
        {
            Assert.Empty(after.WmsBins);
            Assert.Empty(after.WmsOperations);
            Assert.Empty(after.Receipts);
            Assert.DoesNotContain(after.Audit, row => row.EventType is SpacePublishAuditEventType.WmsApplyObserved
                or SpacePublishAuditEventType.ReconciliationRequired);
        }
        else
        {
            AssertNativeWms(after, versions);
            if (boundary == "runtime")
            {
                Assert.Equal(BackendEffectState(whileHeld, "wms"), BackendEffectState(after, "wms"));
                Assert.Equal(whileHeld.Attempt.WmsCommittedAtUtc, after.Attempt.WmsCommittedAtUtc);
            }
        }

        using var obsolete = await SendPublishSubmission(host, actor, submission);
        Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        var suspendedPass = await RunPublishWorkerAsync(fixtureRoot, 2);
        Assert.Null(suspendedPass.Error);
        Assert.Equal(0, suspendedPass.Processed);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, signedPause);
        if (!finished)
            await WaitPublishJobDue(observer, after.Job.NextAttemptAtUtc);
        var resumed = await RunPublishWorkerAsync(fixtureRoot, 3);
        Assert.Null(resumed.Error);
        Assert.Equal(finished ? 0 : 1, resumed.Processed);
        var final = await BackendWriteSet(writer, space, ids);
        AssertCompletedNativePublish(final, versions);
        Assert.Equal(finished ? 1 : 2, final.JobAttempts.Length);
        if (after.WmsOperations.Length != 0)
        {
            Assert.Equal(BackendEffectState(after, "wms"), BackendEffectState(final, "wms"));
            Assert.Equal(after.Attempt.WmsCommittedAtUtc, final.Attempt.WmsCommittedAtUtc);
            Assert.Equal(Assert.Single(after.Batches).Id, Assert.Single(final.Batches).Id);
            Assert.Equal(Assert.Single(after.Receipts).Id, Assert.Single(final.Receipts).Id);
        }
        await AssertOriginalPublishReplay(host, writer, submission, ids);
        await DeviceEvidence($"space-publish-native-{boundary}-{(effectFirst ? "effect-first" : "pause-first")}-resumed.json",
            new { FinishedBeforePause = finished, Final = final, ObsoleteHttp = (int)obsolete.StatusCode, ResumedWorker = resumed });

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    [CurrentPermissionPostgresFact]
    public async Task PGT017_space_publish_native_effect_checks_tenant_expiry_before_commit()
    {
        await using var writer = await Seed();
        await GrantSpaceDesignActions(writer, "model:publish", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedBackendVersions(writer, space, site);
        var root = NewPublishFixtureRoot();
        await using var host = await StartPublishWorkerHost(connection, writer.Tenant, site, root, "runtime-entry");
        var actor = await Login(host, writer);
        var submission = await PreparePublishSubmission(writer, space, execution, site, host, actor, versions.Releases, "publish");
        using var queued = await SendPublishSubmission(host, actor, submission);
        var ids = await CheckPublishSubmission(space, writer, versions.Releases, submission, "publish", queued, replay: false);
        var worker = RunPublishWorkerAsync(root, 1);
        await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "native-ready");
        var before = await BackendWriteSet(writer, space, ids);
        AssertNativeWms(before, versions);

        await using var observer = new NpgsqlConnection(connection);
        await using var blocker = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        await blocker.OpenAsync();
        await using var setDeadline = new NpgsqlCommand("""
            UPDATE public."Sys_Tenants"
            SET "ExpireDate"=@deadline
            WHERE "Id"=@tenant RETURNING "ExpireDate"
            """, observer);
        setDeadline.Parameters.AddWithValue("tenant", writer.Tenant);
        setDeadline.Parameters.AddWithValue("deadline", DateTime.SpecifyKind(DateTime.Now.AddSeconds(10), DateTimeKind.Unspecified));
        var deadline = Assert.IsType<DateTime>(await setDeadline.ExecuteScalarAsync());
        FenceWait wait;
        DateTime crossedAt;
        await using (var hold = await HoldUnderlayTable(blocker, "Space_Location"))
        {
            SpaceArtifactWorkerFixture.Write(root, 1, "native-continue", true);
            wait = await WaitForFence(observer, blocker.ProcessID, "%INSERT%Space_Location%", null, worker);
            Assert.True(DateTime.Now < deadline);
            do
            {
                await Task.Delay(100);
                crossedAt = DateTime.Now;
            } while (crossedAt <= deadline);
            await hold.RollbackAsync();
        }

        var outcome = await worker;
        Assert.Null(outcome.Error);
        Assert.Equal(1, outcome.Processed);
        var expired = await BackendWriteSet(writer, space, ids);
        Assert.Equal(SpacePublishAttemptStatus.WaitingRetry, expired.Attempt.Status);
        Assert.Equal("SPACE_PUBLISH_TENANT_INACTIVE", expired.Attempt.LastErrorCode);
        Assert.Equal(SpaceJobStatus.Queued, expired.Job.Status);
        Assert.Equal(BackendEffectState(before, "runtime"), BackendEffectState(expired, "runtime"));
        Assert.Equal(BackendEffectState(before, "wms"), BackendEffectState(expired, "wms"));

        await using var extend = new NpgsqlCommand("""UPDATE public."Sys_Tenants" SET "ExpireDate"=NULL WHERE "Id"=@tenant""", observer);
        extend.Parameters.AddWithValue("tenant", writer.Tenant);
        Assert.Equal(1, await extend.ExecuteNonQueryAsync());
        await WaitPublishJobDue(observer, expired.Job.NextAttemptAtUtc);
        var retry = await RunPublishWorkerAsync(root, 2);
        Assert.Null(retry.Error);
        Assert.Equal(1, retry.Processed);
        var final = await BackendWriteSet(writer, space, ids);
        AssertCompletedNativePublish(final, versions);
        Assert.Equal(before.Attempt.WmsCommittedAtUtc, final.Attempt.WmsCommittedAtUtc);
        Assert.Equal(BackendEffectState(before, "wms"), BackendEffectState(final, "wms"));
        await DeviceEvidence("space-publish-native-final-expiry.json", new { Deadline = deadline, CrossedAt = crossedAt,
            Wait = wait, Before = before, RolledBack = expired, Resumed = final });
    }

    [TenantManagementPostgresTheory]
    [InlineData("wms")]
    [InlineData("runtime")]
    public async Task PGT017_space_publish_lost_native_commit_ack_recovers_original_effect(string boundary)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        await GrantSpaceDesignActions(writer, "model:publish", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var versions = await SeedBackendVersions(writer, space, site);
        var root = NewPublishFixtureRoot();
        await using var host = await StartPublishWorkerHost(connection, writer.Tenant, site, root, boundary + "-ack");
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var submission = await PreparePublishSubmission(writer, space, execution, site, host, actor, versions.Releases, "publish");
        using var queued = await SendPublishSubmission(host, actor, submission);
        var ids = await CheckPublishSubmission(space, writer, versions.Releases, submission, "publish", queued, replay: false);
        using var activeResponse = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(activeResponse);

        var finishing = RunPublishWorkerAsync(root, 1);
        await SpaceArtifactWorkerFixture.ReadAsync<bool>(root, 1, "native-ready");
        var committed = await BackendWriteSet(writer, space, ids);
        using var paused = await host.Send(OperationsRequest(manager, HttpMethod.Post, ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.False((await NumberHttpData<TenantManagementReceiptV1>(paused)).Snapshot.Enable);
        SpaceArtifactWorkerFixture.Write(root, 1, "native-continue", true);
        var first = await finishing;
        Assert.Null(first.Error);
        Assert.Equal(1, first.Processed);
        var recovered = await BackendWriteSet(writer, space, ids);
        if (boundary == "wms")
        {
            Assert.Equal(SpaceJobStatus.Queued, recovered.Job.Status);
            Assert.Equal(SpacePublishStep.ActivateRuntime, recovered.Attempt.CurrentStep);
            Assert.Equal("SPACE_PUBLISH_TENANT_INACTIVE", recovered.Attempt.LastErrorCode);
            Assert.Equal(BackendEffectState(committed, "wms"), BackendEffectState(recovered, "wms"));
            AssertNativeWms(recovered, versions);
            Assert.Empty(recovered.Locations);
        }
        else
        {
            AssertNativeProjection(committed, versions);
            AssertCompletedNativePublish(recovered, versions);
            Assert.Equal(BackendEffectState(committed, "runtime"), BackendEffectState(recovered, "runtime"));
            Assert.EndsWith(":completed-recovered", recovered.Audit[^1].DeduplicationKey);
        }

        await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync();
        if (boundary == "wms")
            await WaitPublishJobDue(observer, recovered.Job.NextAttemptAtUtc);
        var retry = await RunPublishWorkerAsync(root, 2);
        Assert.Null(retry.Error);
        Assert.Equal(boundary == "wms" ? 1 : 0, retry.Processed);
        var final = await BackendWriteSet(writer, space, ids);
        AssertCompletedNativePublish(final, versions);
        Assert.Equal(BackendEffectState(committed, "wms"), BackendEffectState(final, "wms"));
        if (boundary == "runtime")
            Assert.Equal(BackendEffectState(committed, "runtime"), BackendEffectState(final, "runtime"));
        await AssertOriginalPublishReplay(host, writer, submission, ids);
        await DeviceEvidence("space-publish-native-" + boundary + "-unknown-ack.json",
            new { CommitSeenBeforeAcknowledgement = committed, RecoveredDuringPause = recovered, Resumed = final, WorkerRetry = retry });
    }

    private static void AssertCompletedNativePublish(BackendSnapshot final, BackendVersions versions)
    {
        AssertNativeWms(final, versions);
        AssertNativeProjection(final, versions);
        Assert.Equal(SpacePublishAttemptStatus.Completed, final.Attempt.Status);
        Assert.Equal(SpaceJobStatus.Succeeded, final.Job.Status);
        Assert.Equal(SpaceJobAttemptOutcome.Succeeded, final.JobAttempts[^1].Outcome);
        Assert.Single(final.Audit, entry => entry.EventType == SpacePublishAuditEventType.WmsVerified);
        Assert.Single(final.Audit, entry => entry.EventType == SpacePublishAuditEventType.Completed);
        for (var index = 1; index < final.Audit.Length; index++)
            Assert.Equal(final.Audit[index - 1].EventHash, final.Audit[index].PreviousEventHash);
    }

    private static async Task AssertOriginalPublishReplay(AuthenticationApiProcess host, Harness writer,
        PublishSubmission submission, PublishSubmissionIds ids)
    {
        var actor = await Login(host, writer);
        using var replay = await SendPublishSubmission(host, actor, submission);
        Assert.True(replay.StatusCode == HttpStatusCode.Accepted, await OperationsFailure(replay));
        Assert.Equal("true", Assert.Single(replay.Headers.GetValues("Idempotent-Replay")));
        var body = Assert.IsType<CreateSpacePublishAttemptResponse>(
            await replay.Content.ReadFromJsonAsync<CreateSpacePublishAttemptResponse>());
        Assert.Equal(ids.Operation, body.Attempt.Id);
        Assert.Equal(ids.Job, body.Attempt.JobId);
        Assert.Equal(ids.PlanOrTarget, body.Attempt.PublishPlanId);
    }

    private static async Task WaitPublishJobDue(NpgsqlConnection observer, DateTime nextAttempt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        while (nextAttempt > (await BackendObservationTime(observer)).UtcDateTime)
            await Task.Delay(125, timeout.Token);
    }

    private static string NewPublishFixtureRoot()
    {
        var root = Path.Combine(Environment.GetEnvironmentVariable("CP6_PERMISSION_PG_ROOT")!,
            "publish-worker-fixture", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static Task<AuthenticationApiProcess> StartPublishWorkerHost(string connection, Guid tenant, Guid site, string root, string? relay)
    {
        var settings = SpaceDesignSettings(tenant, site);
        settings["ASPNETCORE_HOSTINGSTARTUPASSEMBLIES"] = "CP6.Tests";
        settings["DOTNET_STARTUP_HOOKS"] = typeof(SpacePublishWorkerFixture).Assembly.Location;
        settings[SpacePublishWorkerFixture.RootVariable] = root;
        if (relay is not null)
            settings[SpacePublishWorkerFixture.RelayVariable] = relay;
        if (Environment.GetEnvironmentVariable("CP6_SPACE_PUBLISH_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(connection, settings);
    }

    private static Task<SpacePublishWorkerFixture.WorkerResult> RunPublishWorkerAsync(string root, int sequence)
    {
        SpaceArtifactWorkerFixture.Write(root, sequence, "request", true);
        return SpaceArtifactWorkerFixture.ReadAsync<SpacePublishWorkerFixture.WorkerResult>(root, sequence, "result");
    }


    private static async Task<BackendVersions> SeedBackendVersions(Harness writer, SpaceContext space, Guid site)
    {
        var floorId = Guid.NewGuid();
        var zoneId = Guid.NewGuid();
        var rackId = Guid.NewGuid();
        var locationId = Guid.NewGuid();
        var elementId = Guid.NewGuid();
        writer.Db.Space_Sites.Add(new Space_Site { Id = site, SiteCode = "W621", SiteName = "Simulated warehouse", WarehouseCd = "W621" });
        writer.Db.Space_Floors.Add(new Space_Floor
        {
            Id = floorId, SiteId = site, Level = 1, FloorCode = "F1", FloorName = "Prior published floor", Height = 5000,
        });
        await writer.Db.SaveChangesAsync();

        var model = SpaceModel.Create(writer.Tenant, site);
        var previous = SpaceModelVersion.CreateDraft(writer.Tenant, model.Id, 1, "Prior published floor");
        var target = SpaceModelVersion.CreateDraft(writer.Tenant, model.Id, 2, "Projected candidate floor", previous.Id);
        var previousFloor = Floor(previous);
        var projectedFloor = Floor(target);
        var zone = SpaceZoneRevision.Create(writer.Tenant, target.Id, zoneId, floorId, "Z1", 1);
        zone.ConfigureShape("""{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""");
        var rack = SpaceRackRevision.Create(writer.Tenant, target.Id, rackId, floorId, zoneId, "R1");
        rack.ConfigureGeometry(1000, 1000, 0, 0, 4000, 1000, 2000);
        var level = SpaceRackLevelRevision.Create(writer.Tenant, target.Id, Guid.NewGuid(), rackId, 1, 0, 1800, 1, 1, 1000, 1000, 100);
        var location = SpaceLocationRevision.Create(writer.Tenant, target.Id, locationId, floorId, rackId,
            "R1-01", 1, 1, 1, 1000, 1800, 1000);
        var element = SpaceElementRevision.Create(writer.Tenant, target.Id, elementId, floorId, SpaceElementTypes.Column,
            """{"schemaVersion":1,"kind":"box","width":1200,"height":1000,"depth":1000}""");
        space.AddRange(model, previous, target, previousFloor, projectedFloor, zone, rack, level, location, element);
        await space.SaveChangesAsync();

        previous.BeginValidation();
        previous.MarkReady(SpaceDesignSampleHash, SpaceValidationRuleSet.Version, SpaceDesignSampleHash);
        previous.BeginPublishing();
        previous.MarkPublished(writer.User, DateTime.UtcNow);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(previous);
        model.ActivateDesignV1();
        model.ReserveDraft(target);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return new(new PublishFixtureVersions(model.Id, previous.Id, target.Id, previous.Id), floorId, locationId, elementId);

        SpaceFloorRevision Floor(SpaceModelVersion version)
        {
            var floor = SpaceFloorRevision.Create(writer.Tenant, version.Id, floorId, site, 1, "F1", version.Name, height: 5000);
            floor.ConfigureBoundary("""{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""", "LOCAL_MM_Z_UP");
            return floor;
        }
    }

    private static async Task<BackendSnapshot> BackendWriteSet(Harness writer, SpaceContext space, PublishSubmissionIds ids) => new(
        await space.Models.AsNoTracking().SingleAsync(),
        await space.Versions.AsNoTracking().OrderBy(row => row.VersionNo).ToArrayAsync(),
        await space.PublishAttempts.AsNoTracking().SingleAsync(row => row.Id == ids.Operation),
        await space.PublishPlans.AsNoTracking().SingleAsync(row => row.Id == ids.PlanOrTarget),
        await space.Jobs.AsNoTracking().SingleAsync(row => row.Id == ids.Job),
        await space.JobAttempts.AsNoTracking().Where(row => row.JobId == ids.Job).OrderBy(row => row.AttemptNo).ToArrayAsync(),
        await space.PublishBatches.AsNoTracking().OrderBy(row => row.BatchNo).ToArrayAsync(),
        await space.WmsReceipts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await space.PublishAuditEvents.AsNoTracking().OrderBy(row => row.EventNo).ToArrayAsync(),
        await writer.Db.WmsBins.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await writer.Db.SpaceWmsOperations.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await writer.Db.Space_Floors.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await writer.Db.Space_Locations.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        await space.RuntimeElements.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync());

    private static async Task<DateTimeOffset> BackendObservationTime(NpgsqlConnection observer)
    {
        await using var command = new NpgsqlCommand("SELECT clock_timestamp()", observer);
        return new DateTimeOffset((DateTime)(await command.ExecuteScalarAsync())!);
    }

    private static string BackendEffectState(BackendSnapshot state, string boundary) =>
        boundary == "wms"
            ? JsonSerializer.Serialize(new { state.WmsBins, state.WmsOperations })
            : JsonSerializer.Serialize(new { state.Model, state.Versions, state.Floors, state.Locations, state.Elements });

    private static void AssertNativeWms(BackendSnapshot snapshot, BackendVersions versions)
    {
        var bin = Assert.Single(snapshot.WmsBins);
        Assert.Equal(versions.Location, bin.Id);
        Assert.Equal("W621", bin.WarehouseCd);
        Assert.Equal("R1-01", bin.LocationCode);
        Assert.Equal(2, bin.Version);
        Assert.True(bin.IsActive);
        var operation = Assert.Single(snapshot.WmsOperations);
        var batch = Assert.Single(snapshot.Batches);
        Assert.Equal(SpacePublishBatchStatus.Verified, batch.Status);
        Assert.Equal(operation.Id.ToString("D"), operation.ExternalOperationId);
        Assert.Equal(operation.ExternalOperationId, batch.ExternalOperationId);
        Assert.Equal(operation.OperationKey, batch.OperationKey);
        Assert.Equal(operation.PayloadHash, batch.PayloadHash);
        Assert.Equal(64, operation.PayloadHash.Length);
        var receipt = Assert.IsType<SpaceWmsBatchResult>(JsonSerializer.Deserialize<SpaceWmsBatchResult>(operation.ResultJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(operation.ExternalOperationId, receipt.ExternalOperationId);
        Assert.Equal(versions.Location, Assert.Single(receipt.Items).LogicalId);
    }

    private static void AssertNativeProjection(BackendSnapshot snapshot, BackendVersions versions)
    {
        Assert.Equal(versions.Releases.Candidate, snapshot.Model.CurrentPublishedVersionId);
        Assert.Equal(64, snapshot.Model.LastMaterializedHash!.Length);
        var prior = Assert.Single(snapshot.Versions, row => row.Id == versions.Releases.Previous);
        var target = Assert.Single(snapshot.Versions, row => row.Id == versions.Releases.Candidate);
        Assert.Equal(SpaceVersionStatus.Superseded, prior.Status);
        Assert.Equal(SpaceVersionStatus.Published, target.Status);
        var floor = Assert.Single(snapshot.Floors);
        Assert.Equal(versions.Floor, floor.Id);
        Assert.Equal("Projected candidate floor", floor.FloorName);
        var location = Assert.Single(snapshot.Locations);
        Assert.Equal(versions.Location, location.Id);
        Assert.Equal(versions.Floor, location.FloorId);
        Assert.Equal("R1-01", location.LocationCode);
        var element = Assert.Single(snapshot.Elements);
        Assert.Equal(versions.Element, element.LogicalId);
        Assert.Equal(versions.Releases.Candidate, element.ModelVersionId);
    }

    private sealed record BackendSnapshot(SpaceModel Model, SpaceModelVersion[] Versions, SpacePublishAttempt Attempt,
        SpacePublishPlan Plan, SpaceJob Job, SpaceJobAttempt[] JobAttempts, SpacePublishBatch[] Batches, SpaceWmsReceipt[] Receipts, SpacePublishAuditEvent[] Audit,
        CP6.Entity.DomainModels.Wms.WmsBin[] WmsBins, CP6.Entity.DomainModels.Wms.SpaceWmsOperation[] WmsOperations,
        Space_Floor[] Floors, Space_Location[] Locations, SpaceRuntimeElement[] Elements);

    private sealed record BackendVersions(PublishFixtureVersions Releases, Guid Floor, Guid Location, Guid Element);
}
