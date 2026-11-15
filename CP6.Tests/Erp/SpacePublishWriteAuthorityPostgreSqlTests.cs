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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace CP6.Tests.Erp;

public sealed partial class OrderPriceCorrectionPostgreSqlTests
{
    [TenantManagementPostgresTheory]
    [InlineData("publish", true)]
    [InlineData("publish", false)]
    [InlineData("retry", true)]
    [InlineData("retry", false)]
    [InlineData("republish", true)]
    [InlineData("republish", false)]
    public async Task PGT017_space_publish_submission_and_pause_share_current_authority(string operation, bool businessFirst)
    {
        await using var writer = await Seed();
        await using var administrator = await TenantManagementSeed();
        var historical = operation == "republish";
        var action = historical ? "model:rollback" : "model:publish";
        var menuId = await GrantSpaceDesignActions(writer, action, "model:read");
        var granted = await writer.Db.Sys_RoleActions.AsNoTracking()
            .Where(row => row.TenantId == writer.Tenant && row.RoleId == 72 && row.MenuId == menuId)
            .Select(row => row.ActionCode).OrderBy(value => value).ToArrayAsync();
        Assert.Equal(new[] { action, "model:read" }.Order(StringComparer.Ordinal), granted);

        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var release = await SeedPublishVersions(writer, space, site, historical);
        await using var host = await StartPublishSubmissionHost(connection, writer.Tenant, site);
        var actor = await Login(host, writer);
        var manager = await Login(host, administrator);
        var input = await PreparePublishSubmission(writer, space, execution, site, host, actor, release, operation);
        var before = await PublishSubmissionWriteSet(writer, space);
        using var current = await host.Send(OperationsRequest(manager, HttpMethod.Get, ManagementPath(writer.Tenant)));
        var active = await NumberHttpData<TenantManagementSnapshotV1>(current);

        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();

        var table = historical ? "Space_HistoricalRepublish" : "Space_PublishAttempt";
        var direction = businessFirst ? "business-first" : "pause-first";
        Task<HttpResponseMessage> pending;
        Task<HttpResponseMessage> pause;
        FenceWait ownerWait, pauseWait;
        await using (var blocked = await HoldUnderlayTable(business, table))
        await using (var held = await HoldUnderlayTenant(control, writer.Tenant))
        {
            if (businessFirst)
            {
                pending = SendPublishSubmission(host, actor, input);
                ownerWait = await WaitForFence(observer, business.ProcessID, $"%{table}%", null, pending);
                pause = Pause();
                pauseWait = await SerialNativeWait(observer, ownerWait.Backend, control.ProcessID,
                    $"space-{operation}-business-first", pause);
            }
            else
            {
                pause = Pause();
                pauseWait = await WaitForFence(observer, control.ProcessID, "%UPDATE%Sys_Tenants%", null, pause);
                pending = SendPublishSubmission(host, actor, input);
                ownerWait = await SerialNativeWait(observer, pauseWait.Backend, business.ProcessID,
                    $"space-{operation}-pause-first", pending);
            }
            await held.RollbackAsync();
            if (pauseWait.Blocker == control.ProcessID)
                await pause;
            await blocked.RollbackAsync();
        }

        using var response = await pending;
        using var paused = await pause;
        var after = await PublishSubmissionWriteSet(writer, space);
        var expected = businessFirst ? HttpStatusCode.Accepted : HttpStatusCode.Unauthorized;
        await DeviceEvidence($"space-publish-{operation}-{direction}.json", new
        {
            Action = "space:" + action, OwnerWait = ownerWait, PauseWait = pauseWait,
            Expected = (int)expected, Actual = (int)response.StatusCode, Before = before, After = after,
        });
        Assert.True(response.StatusCode == expected, response.StatusCode == expected ? null : await OperationsFailure(response));
        PublishSubmissionIds? committed = null;
        if (businessFirst)
        {
            Assert.Equal(ownerWait.Backend, pauseWait.Blocker);
            committed = await CheckPublishSubmission(space, writer, release, input, operation, response, replay: false);
        }
        else
        {
            Assert.Equal(pauseWait.Backend, ownerWait.Blocker);
            Assert.Equal(before, after);
        }

        using var obsolete = await SendPublishSubmission(host, actor, input);
        Assert.Equal(HttpStatusCode.Unauthorized, obsolete.StatusCode);
        await ResumeSourceUploadTenant(host, manager, writer.Tenant, paused);
        actor = await Login(host, writer);
        using var recovered = await SendPublishSubmission(host, actor, input);
        var recoveredIds = await CheckPublishSubmission(space, writer, release, input, operation, recovered, replay: businessFirst);
        var final = await PublishSubmissionWriteSet(writer, space);
        if (businessFirst)
        {
            Assert.Equal(committed, recoveredIds);
            Assert.Equal(after, final);
        }
        using var repeat = await SendPublishSubmission(host, actor, input);
        Assert.Equal(recoveredIds, await CheckPublishSubmission(space, writer, release, input, operation, repeat, replay: true));
        Assert.Equal(final, await PublishSubmissionWriteSet(writer, space));

        Task<HttpResponseMessage> Pause() => host.Send(OperationsRequest(manager, HttpMethod.Post,
            ManagementPath(writer.Tenant, "/commands"),
            new TenantManagementCommandV1(Guid.NewGuid().ToString("N"), active.Revision, "suspend", null)));
    }

    [TenantManagementPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PGT017_space_publish_rollback_role_revocation_shares_reservation_authority(bool businessFirst)
    {
        await using var writer = await Seed();
        var menu = await GrantSpaceDesignActions(writer, "model:rollback", "model:read");
        var connection = writer.Db.Database.GetConnectionString()!;
        var execution = new SpaceDesignExecution(writer.Tenant, writer.User);
        var site = Guid.NewGuid();
        await using var space = SpaceDesignContext(connection, execution);
        await space.Database.MigrateAsync();
        var release = await SeedPublishVersions(writer, space, site, historical: true);
        await using var host = await StartPublishSubmissionHost(connection, writer.Tenant, site);
        var actor = await Login(host, writer);
        var input = await PreparePublishSubmission(writer, space, execution, site, host, actor, release, "republish");
        var before = await PublishSubmissionWriteSet(writer, space);

        await using var control = new NpgsqlConnection(connection);
        await using var business = new NpgsqlConnection(connection);
        await using var observer = new NpgsqlConnection(connection);
        await control.OpenAsync();
        await business.OpenAsync();
        await observer.OpenAsync();
        Task<HttpResponseMessage> pending;
        FenceWait ownerWait;
        FenceWait? revokeWait = null;
        var order = businessFirst ? "business-first" : "permission-first";
        await using (var blocked = await HoldUnderlayTable(business, "Space_HistoricalRepublish"))
        await using (var revoking = await control.BeginTransactionAsync())
        {
            await using var remove = new NpgsqlCommand("""
                SELECT cp6_authentication_v1.acquire();
                DELETE FROM public."Sys_RoleAction"
                WHERE "TenantId"=@tenant AND "RoleId"=72 AND "MenuId"=@menu AND "ActionCode"='model:rollback';
                """, control, revoking);
            remove.Parameters.AddWithValue("tenant", writer.Tenant);
            remove.Parameters.AddWithValue("menu", menu);

            if (businessFirst)
            {
                pending = SendPublishSubmission(host, actor, input);
                ownerWait = await WaitForFence(observer, business.ProcessID, "%Space_HistoricalRepublish%", null, pending);
                var revoke = remove.ExecuteNonQueryAsync();
                revokeWait = await WaitForFence(observer, ownerWait.Backend, "%cp6_authentication_v1.acquire%", null, revoke);
                await blocked.RollbackAsync();
                Assert.Equal(1, await revoke);
                await revoking.CommitAsync();
            }
            else
            {
                Assert.Equal(1, await remove.ExecuteNonQueryAsync());
                pending = SendPublishSubmission(host, actor, input);
                ownerWait = await SerialNativeWait(observer, control.ProcessID, business.ProcessID, $"space-republish-{order}", pending);
                await revoking.CommitAsync();
                await blocked.RollbackAsync();
            }
        }

        using var response = await pending;
        var after = await PublishSubmissionWriteSet(writer, space);
        var expected = businessFirst ? HttpStatusCode.Accepted : HttpStatusCode.Forbidden;
        await DeviceEvidence($"space-republish-rollback-{order}.json", new
        {
            Action = "space:model:rollback", OwnerWait = ownerWait, RevokeWait = revokeWait,
            Expected = (int)expected, Actual = (int)response.StatusCode, Before = before, After = after,
        });
        Assert.Equal(expected, response.StatusCode);
        PublishSubmissionIds? committed = null;
        if (businessFirst)
        {
            Assert.Equal(control.ProcessID, revokeWait!.Backend);
            Assert.Equal(ownerWait.Backend, revokeWait.Blocker);
            committed = await CheckPublishSubmission(space, writer, release, input, "republish", response, replay: false);
        }
        else
        {
            Assert.Equal(control.ProcessID, ownerWait.Blocker);
            Assert.Equal(before, after);
        }

        Assert.Equal("model:read", await writer.Db.Sys_RoleActions.AsNoTracking()
            .Where(row => row.TenantId == writer.Tenant && row.RoleId == 72 && row.MenuId == menu)
            .Select(row => row.ActionCode).SingleAsync());
        using var profile = await host.Send(OperationsRequest(actor, HttpMethod.Get, "api/auth/profile"));
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        using var denied = await SendPublishSubmission(host, actor, input);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(after, await PublishSubmissionWriteSet(writer, space));

        await GrantSpaceDesignActions(writer, "model:rollback");
        using var recovered = await SendPublishSubmission(host, actor, input);
        var recoveredIds = await CheckPublishSubmission(space, writer, release, input, "republish", recovered, replay: businessFirst);
        if (businessFirst)
        {
            Assert.Equal(committed, recoveredIds);
            Assert.Equal(after, await PublishSubmissionWriteSet(writer, space));
        }
    }

    private static Task<AuthenticationApiProcess> StartPublishSubmissionHost(string connection, Guid tenant, Guid site)
    {
        var settings = SpaceDesignSettings(tenant, site);
        if (Environment.GetEnvironmentVariable("CP6_SPACE_PUBLISH_HTTP_PORT") is { } port)
            settings["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}";
        return AuthenticationApiProcess.Start(connection, settings);
    }

    private static async Task<PublishFixtureVersions> SeedPublishVersions(Harness writer, SpaceContext space, Guid site, bool historical)
    {
        writer.Db.Space_Sites.Add(new Space_Site { Id = site, SiteCode = "W604", SiteName = "Simulated publish site", WarehouseCd = "W604" });
        await writer.Db.SaveChangesAsync();
        var model = SpaceModel.Create(writer.Tenant, site);
        space.Models.Add(model);
        await space.SaveChangesAsync();

        var previous = SpaceModelVersion.CreateDraft(writer.Tenant, model.Id, 1, "Previous published floor");
        var candidate = SpaceModelVersion.CreateDraft(writer.Tenant, model.Id, 2, "Candidate floor", previous.Id);
        var floor = Guid.NewGuid();
        space.AddRange(previous, candidate, CreatePublishFixtureFloor(previous), CreatePublishFixtureFloor(candidate));
        await space.SaveChangesAsync();
        MarkFixtureVersionPublished(previous, writer.User);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(previous);
        model.ActivateDesignV1();
        model.ReserveDraft(candidate);
        if (historical)
        {
            MarkFixtureVersionPublished(candidate, writer.User);
            previous.MarkSuperseded();
            model.SetPublishedVersion(candidate, SpaceDesignSampleHash);
        }
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return new PublishFixtureVersions(model.Id, previous.Id, candidate.Id, historical ? candidate.Id : previous.Id);

        SpaceFloorRevision CreatePublishFixtureFloor(SpaceModelVersion version)
        {
            var item = SpaceFloorRevision.Create(writer.Tenant, version.Id, floor, site, 1, "F1", "Fixture floor", height: 5000);
            item.ConfigureBoundary("""{"schemaVersion":1,"points":[[0,0],[10000,0],[10000,8000],[0,8000]]}""", "LOCAL_MM_Z_UP");
            return item;
        }

        static void MarkFixtureVersionPublished(SpaceModelVersion version, Guid actor)
        {
            version.BeginValidation();
            version.MarkReady(SpaceDesignSampleHash, SpaceValidationRuleSet.Version, SpaceDesignSampleHash);
            version.BeginPublishing();
            version.MarkPublished(actor, DateTime.UtcNow);
        }
    }

    private static async Task<PublishSubmission> PreparePublishSubmission(Harness writer, SpaceContext space, SpaceDesignExecution execution,
        Guid site, AuthenticationApiProcess host, Dictionary<string, string> actor, PublishFixtureVersions versions, string operation)
    {
        var key = Guid.NewGuid().ToString("N");
        if (operation == "republish")
        {
            return new($"{SpaceDesignPath}/versions/{versions.Previous:D}/republish", key,
                new StartSpaceHistoricalRepublishRequest(versions.Published, "Restore approved history", "W604-SIMULATED", "Historical candidate"));
        }

        using var dependencies = new ServiceCollection()
            .AddSingleton<ISpaceWarehouseResolver>(new Cp6SpaceWarehouseResolver(writer.Db))
            .AddSingleton<ISpaceWmsAdapter>(new Cp6SpaceWmsAdapter(writer.Db))
            .BuildServiceProvider();
        var profiles = new DefaultSpaceValidationProfileProvider(dependencies);
        var clock = new SystemSpaceClock();
        var engine = new SpaceValidationEngine();
        await using (var validation = SpaceDesignContext(writer.Db.Database.GetConnectionString()!, execution))
        {
            await new SpaceValidationService(validation, execution, clock, new UnderlayTestSiteAccess(site), profiles, engine)
                .RequestValidationAsync(versions.Candidate);
        }
        await using (var worker = SpaceDesignContext(writer.Db.Database.GetConnectionString()!, execution))
        {
            var leases = new EfSpaceJobLeaseStore(worker, clock);
            var runner = new SpaceJobProcessorRunner(leases, [new SpaceValidationJobProcessor(worker, clock, profiles, engine)]);
            Assert.True(await runner.RunNextAsync(SpaceJobType.Validate, "w604-fixture-validation"));
        }
        using var previewResponse = await host.Send(OperationsRequest(actor, HttpMethod.Get,
            $"{SpaceDesignPath}/versions/{versions.Candidate:D}/publish-preview?includeNoOp=true"));
        Assert.True(previewResponse.StatusCode == HttpStatusCode.OK,
            previewResponse.StatusCode == HttpStatusCode.OK ? null : await OperationsFailure(previewResponse));
        var preview = Assert.IsType<SpacePublishPreviewDto>(await previewResponse.Content.ReadFromJsonAsync<SpacePublishPreviewDto>());
        Assert.True(preview.Publishable);
        Assert.Equal(versions.Published, preview.BaseVersionId);
        Assert.True(preview.ItemCount > 0);
        var create = new PublishSubmission($"{SpaceDesignPath}/versions/{versions.Candidate:D}/publish-attempts", key,
            new CreateSpacePublishAttemptRequest(versions.Published, preview.ValidationRunId, preview.PlanHash,
                "W604-SIMULATED", preview.WarningAcknowledgementHash));
        if (operation == "publish")
            return create;

        using var queued = await SendPublishSubmission(host, actor, create);
        Assert.True(queued.StatusCode == HttpStatusCode.Accepted,
            queued.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(queued));
        var initial = Assert.IsType<CreateSpacePublishAttemptResponse>(await queued.Content.ReadFromJsonAsync<CreateSpacePublishAttemptResponse>());
        var attempt = await space.PublishAttempts.SingleAsync(row => row.Id == initial.Attempt.Id);
        var original = await space.Jobs.SingleAsync(row => row.Id == initial.Attempt.JobId);
        attempt.WaitForRetry(SpacePublishStep.Preflight, "FIXTURE_RETRY", "Simulated interruption before WMS application.");
        original.RequestCancellation(writer.User, DateTime.UtcNow);
        await space.SaveChangesAsync();
        space.ChangeTracker.Clear();
        return new($"{SpaceDesignPath}/publish-attempts/{attempt.Id:D}/retry", Guid.NewGuid().ToString("N"),
            new RetrySpacePublishAttemptRequest("Resume approved publishing", null), initial.Attempt);
    }

    private static async Task<HttpResponseMessage> SendPublishSubmission(AuthenticationApiProcess host, Dictionary<string, string> actor,
        PublishSubmission input)
    {
        using var request = OperationsRequest(actor, HttpMethod.Post, input.Path, input.Body);
        request.Headers.Add("Idempotency-Key", input.Key);
        return await host.Send(request);
    }

    private static async Task<PublishSubmissionIds> CheckPublishSubmission(SpaceContext space, Harness writer, PublishFixtureVersions versions,
        PublishSubmission input, string operation, HttpResponseMessage response, bool replay)
    {
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            response.StatusCode == HttpStatusCode.Accepted ? null : await OperationsFailure(response));
        Assert.Equal(replay ? "true" : "false", Assert.Single(response.Headers.GetValues("Idempotent-Replay")));
        var model = await space.Models.AsNoTracking().SingleAsync(row => row.Id == versions.Model);
        var published = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == versions.Published);
        Assert.Equal(published.Id, model.CurrentPublishedVersionId);
        Assert.Equal(SpaceVersionStatus.Published, published.Status);
        Assert.Empty(await space.PublishBatches.AsNoTracking().ToArrayAsync());
        Assert.Empty(await space.RuntimeElements.AsNoTracking().ToArrayAsync());
        Assert.Empty(await writer.Db.SpaceWmsOperations.AsNoTracking().ToArrayAsync());
        if (operation == "republish")
        {
            var body = Assert.IsType<StartSpaceHistoricalRepublishResponse>(await response.Content.ReadFromJsonAsync<StartSpaceHistoricalRepublishResponse>());
            var republish = await space.HistoricalRepublishes.AsNoTracking().SingleAsync();
            var clone = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == model.ActiveDraftVersionId);
            Assert.Equal(body.Republish.Id, republish.Id);
            Assert.Equal(body.Republish.TargetVersionId, clone.Id);
            Assert.Equal(body.Republish.JobId, republish.JobId);
            Assert.Equal(versions.Previous, republish.HistoricalVersionId);
            Assert.Equal(versions.Published, republish.ExpectedPublishedVersionId);
            Assert.Equal(input.Key, republish.BusinessIdempotencyKey);
            Assert.Equal(writer.User, republish.RequestedBy);
            Assert.Equal(SpaceHistoricalRepublishStatus.Requested, republish.Status);
            Assert.Equal(SpaceVersionStatus.Initializing, clone.Status);
            Assert.Equal(versions.Previous, clone.BasedOnVersionId);
            Assert.Equal(3, clone.VersionNo);
            Assert.Equal(SpaceVersionStatus.Superseded,
                (await space.Versions.AsNoTracking().SingleAsync(row => row.Id == versions.Previous)).Status);
            Assert.Empty(await space.PublishPlans.AsNoTracking().ToArrayAsync());
            Assert.Empty(await space.PublishAttempts.AsNoTracking().ToArrayAsync());
            Assert.Empty(await space.PublishAuditEvents.AsNoTracking().ToArrayAsync());
            var job = await space.Jobs.AsNoTracking().SingleAsync();
            Assert.Equal(republish.Id, job.SubjectId);
            Assert.Equal(republish.RequestHash, job.InputHash);
            Assert.Equal(SpaceJobType.HistoricalRepublish, job.JobType);
            Assert.Equal(SpaceJobStatus.Queued, job.Status);
            Assert.Equal(job.Id, republish.JobId);
            return new(republish.Id, job.Id, clone.Id);
        }

        var queued = operation == "publish"
            ? Assert.IsType<CreateSpacePublishAttemptResponse>(await response.Content.ReadFromJsonAsync<CreateSpacePublishAttemptResponse>()).Attempt
            : Assert.IsType<RetrySpacePublishAttemptResponse>(await response.Content.ReadFromJsonAsync<RetrySpacePublishAttemptResponse>()).Attempt;
        var attempt = await space.PublishAttempts.AsNoTracking().SingleAsync();
        var plan = await space.PublishPlans.AsNoTracking().SingleAsync();
        var target = await space.Versions.AsNoTracking().SingleAsync(row => row.Id == versions.Candidate);
        Assert.Equal(queued.Id, attempt.Id);
        Assert.Equal(queued.JobId, attempt.JobId);
        Assert.Equal(plan.Id, attempt.PublishPlanId);
        Assert.Equal(plan.PlanHash, queued.PlanHash);
        Assert.Equal(versions.Candidate, plan.TargetVersionId);
        Assert.Equal(versions.Published, plan.BaseVersionId);
        Assert.Equal(versions.Candidate, model.ActiveDraftVersionId);
        Assert.Equal(SpaceVersionStatus.Publishing, target.Status);
        Assert.True(attempt.OwnsPublishSlot);
        Assert.Null(attempt.WmsCommittedAtUtc);
        Assert.Null(attempt.RuntimeActivatedAtUtc);
        Assert.Empty(await space.HistoricalRepublishes.AsNoTracking().ToArrayAsync());
        var jobs = await space.Jobs.AsNoTracking().Where(row => row.SubjectId == attempt.Id).ToArrayAsync();
        var queuedJob = Assert.Single(jobs, row => row.Status == SpaceJobStatus.Queued);
        Assert.Equal(writer.User, queuedJob.RequestedBy);
        Assert.Equal(attempt.JobId, queuedJob.Id);
        Assert.Equal(SpaceJobType.Publish, queuedJob.JobType);
        var audit = await space.PublishAuditEvents.AsNoTracking().OrderBy(value => value.EventNo).ToArrayAsync();
        Assert.Equal(SpacePublishAuditEventType.Queued, audit[0].EventType);
        Assert.Equal(writer.User, audit[^1].ActorId);
        Assert.Equal(queuedJob.Id, audit[^1].JobId);
        if (operation == "publish")
        {
            var submitted = Assert.IsType<CreateSpacePublishAttemptRequest>(input.Body);
            Assert.Equal(input.Key, attempt.BusinessIdempotencyKey);
            Assert.Equal(submitted.PlanHash, plan.PlanHash);
            Assert.Equal(submitted.ValidationRunId, plan.ValidationRunId);
            Assert.Equal(plan.PlanHash, queuedJob.InputHash);
            Assert.Single(jobs);
            Assert.Single(audit);
            Assert.Equal(0, attempt.ManualRetryCount);
        }
        else
        {
            var initial = input.Original!;
            Assert.Equal(initial.Id, attempt.Id);
            Assert.Equal(initial.PublishPlanId, plan.Id);
            Assert.Equal(initial.JobId, queuedJob.RetryOfJobId);
            Assert.Equal(SpaceJobStatus.Cancelled, Assert.Single(jobs, row => row.Id == initial.JobId).Status);
            Assert.Equal(2, jobs.Length);
            Assert.Equal(2, audit.Length);
            Assert.Equal(initial.JobId, audit[0].JobId);
            Assert.Equal(SpacePublishAuditEventType.ManualRetryRequested, audit[1].EventType);
            Assert.Equal(audit[0].EventHash, audit[1].PreviousEventHash);
            Assert.Equal(1, attempt.ManualRetryCount);
        }
        return new(attempt.Id, queuedJob.Id, plan.Id);
    }

    private static async Task<string> PublishSubmissionWriteSet(Harness writer, SpaceContext space) => JsonSerializer.Serialize(new
    {
        Models = await space.Models.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Versions = await space.Versions.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Plans = await space.PublishPlans.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Attempts = await space.PublishAttempts.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Jobs = await space.Jobs.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Validation = await space.ValidationRuns.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Audit = await space.PublishAuditEvents.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Republish = await space.HistoricalRepublishes.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        Reconciliation = await space.ReconciliationIssues.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
        WmsOperations = await writer.Db.SpaceWmsOperations.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
    });

    private sealed record PublishFixtureVersions(Guid Model, Guid Previous, Guid Candidate, Guid Published);
    private sealed record PublishSubmission(string Path, string Key, object Body, SpacePublishAttemptDto? Original = null);
    private sealed record PublishSubmissionIds(Guid Operation, Guid Job, Guid PlanOrTarget);
}
