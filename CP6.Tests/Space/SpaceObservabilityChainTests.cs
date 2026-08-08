using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Options;
using CP6.Core.Services;
using CP6.Core.Services.Common;
using CP6.Core.Services.Integration;
using CP6.Core.Services.Space;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Space;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Space;
using CP6.WebApi.BackgroundServices;
using CP6.WebApi.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceObservabilityChainTests
{
    private static readonly Guid TenantA =
        Guid.Parse("10000000-0000-0000-0000-00000000000A");
    private static readonly Guid ActorId =
        Guid.Parse("20000000-0000-0000-0000-00000000000A");
    private static readonly Guid CorrelationId =
        Guid.Parse("30000000-0000-0000-0000-00000000000A");

    [Fact]
    public async Task Http_publish_failure_then_worker_retry_preserves_one_observability_chain()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(
                $"space-observability-chain-{Guid.NewGuid():N}",
                new InMemoryDatabaseRoot())
            .Options;
        var consumer = new FailOnceWmsLocationConsumer();
        using var provider = BuildProvider(options, consumer);

        Guid floorId;
        using (var seedScope = provider.CreateScope())
        {
            seedScope.ServiceProvider
                .GetRequiredService<ITenantContext>()
                .CurrentTenantId = TenantA;
            var db = seedScope.ServiceProvider
                .GetRequiredService<CP6Context>();
            floorId = SeedPublishableFloor(db);
        }

        using (var requestScope = provider.CreateScope())
        {
            var requestServices = requestScope.ServiceProvider;
            var tenant = requestServices
                .GetRequiredService<ITenantContext>();
            tenant.CurrentTenantId = TenantA;
            var http = NewAuthenticatedSpaceRequest(
                requestServices,
                TenantA,
                ActorId,
                CorrelationId);
            var middleware = new SpaceExecutionContextMiddleware(
                async context =>
                {
                    var writer = context.RequestServices
                        .GetRequiredService<ISpaceAuditWriter>();
                    Assert.True(await writer.TryAppendAsync(
                        PublishAudit(
                            floorId,
                            SpaceAuditOutcome.Started)));

                    var published = await context.RequestServices
                        .GetRequiredService<ILocationPublishService>()
                        .PublishFloorAsync(
                            floorId,
                            zoneId: null,
                            user: "alice");
                    Assert.Equal(1, published);

                    Assert.True(await writer.TryAppendAsync(
                        PublishAudit(
                            floorId,
                            SpaceAuditOutcome.Succeeded,
                            published)));
                },
                NullLogger<SpaceExecutionContextMiddleware>.Instance);

            await middleware.InvokeAsync(
                http,
                tenant,
                requestServices.GetRequiredService<
                    ISpaceExecutionContextManager>());

            Assert.Equal(
                CorrelationId.ToString(),
                http.Response.Headers["X-Correlation-ID"].ToString());
            Assert.False(string.IsNullOrWhiteSpace(
                http.Response.Headers["X-Trace-ID"].ToString()));
        }

        IntegrationEvent initialEvent;
        List<Space_AuditEvent> initialAudits;
        using (var inspectScope = provider.CreateScope())
        {
            inspectScope.ServiceProvider
                .GetRequiredService<ITenantContext>()
                .CurrentTenantId = TenantA;
            var db = inspectScope.ServiceProvider
                .GetRequiredService<CP6Context>();
            initialEvent = await db.IntegrationEvents
                .AsNoTracking()
                .SingleAsync();
            initialAudits = await db.SpaceAuditEvents
                .IgnoreQueryFilters()
                .AsNoTracking()
                .OrderBy(audit => audit.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(IntegrationEventStatus.Failed, initialEvent.Status);
            Assert.Equal("SPACE_ADAPTER_REJECTED", initialEvent.LastError);
            Assert.Equal(CorrelationId, initialEvent.CorrelationId);
            Assert.Equal(TenantA, initialEvent.TenantId);
            Assert.NotNull(initialEvent.JobId);
            Assert.NotNull(initialEvent.PublishAttemptId);
            Assert.Equal(1, initialEvent.Attempts);
            Assert.Equal(2, initialAudits.Count);

            var userStarted = Assert.Single(
                initialAudits,
                audit => audit.Outcome == SpaceAuditOutcome.Started);
            var userSucceeded = Assert.Single(
                initialAudits,
                audit => audit.Outcome == SpaceAuditOutcome.Succeeded);
            Assert.Equal(SpaceExecutionContext.UserActor, userStarted.ActorType);
            Assert.Equal(SpaceExecutionContext.UserActor, userSucceeded.ActorType);
            Assert.Null(userStarted.JobId);
            Assert.Null(userStarted.PublishAttemptId);
            Assert.Equal(initialEvent.JobId, userSucceeded.JobId);
            Assert.Equal(
                initialEvent.PublishAttemptId,
                userSucceeded.PublishAttemptId);
            Assert.Equal(userStarted.TraceId, userSucceeded.TraceId);

            var due = await db.IntegrationEvents.SingleAsync();
            due.NextRetryAt = DateTime.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync();
        }

        var worker = new IntegrationEventRetryWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new IntegrationEventOptions
            {
                MaxAttempts = 3,
                BackoffSeconds = [1, 2, 3],
                SpaceRetryLeaseSeconds = 30,
                SpaceRetryHeartbeatSeconds = 5,
                SpaceDeadLetterNotificationLeaseSeconds = 30,
            }),
            NullLogger<IntegrationEventRetryWorker>.Instance);
        await worker.ProcessOnceAsync();

        using var finalScope = provider.CreateScope();
        finalScope.ServiceProvider
            .GetRequiredService<ITenantContext>()
            .CurrentTenantId = TenantA;
        var finalDb = finalScope.ServiceProvider
            .GetRequiredService<CP6Context>();
        var finalEvent = await finalDb.IntegrationEvents
            .AsNoTracking()
            .SingleAsync();
        var audits = await finalDb.SpaceAuditEvents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(audit => audit.OccurredAtUtc)
            .ToListAsync();

        Assert.Equal(IntegrationEventStatus.Success, finalEvent.Status);
        Assert.Equal(2, finalEvent.Attempts);
        Assert.Null(finalEvent.LastError);
        Assert.Null(finalEvent.NextRetryAt);
        Assert.Null(finalEvent.RetryLeaseId);
        Assert.Equal(initialEvent.JobId, finalEvent.JobId);
        Assert.Equal(
            initialEvent.PublishAttemptId,
            finalEvent.PublishAttemptId);

        Assert.Equal(2, consumer.Calls);
        Assert.Null(consumer.RetryFences[0]);
        var retryFence = Assert.IsType<SpaceRetryFence>(
            consumer.RetryFences[1]);
        Assert.Equal(finalEvent.Id, retryFence.EventId);

        Assert.Equal(4, audits.Count);
        Assert.All(
            audits,
            audit =>
            {
                Assert.Equal(TenantA, audit.TenantId);
                Assert.Equal(CorrelationId, audit.CorrelationId);
            });
        Assert.Single(
            audits.Select(audit => audit.CorrelationId)
                .Append(finalEvent.CorrelationId)
                .Distinct());
        Assert.Single(
            audits.Select(audit => audit.TenantId)
                .Append(finalEvent.TenantId)
                .Distinct());

        var retryAudits = audits
            .Where(audit =>
                audit.Action == "space.integration-event.retry")
            .ToList();
        Assert.Equal(2, retryAudits.Count);
        Assert.Contains(
            retryAudits,
            audit => audit.Outcome == SpaceAuditOutcome.Started);
        Assert.Contains(
            retryAudits,
            audit => audit.Outcome == SpaceAuditOutcome.Succeeded);
        Assert.All(
            retryAudits,
            audit =>
            {
                Assert.Equal(
                    SpaceExecutionContext.SystemActor,
                    audit.ActorType);
                Assert.Equal(
                    "space-worker:integration-event-retry",
                    audit.ActorId);
                Assert.Equal(initialEvent.JobId, audit.JobId);
                Assert.Equal(
                    initialEvent.PublishAttemptId,
                    audit.PublishAttemptId);
                Assert.Equal(2, audit.AttemptNo);
                Assert.NotNull(audit.RunId);
            });
        Assert.Single(retryAudits.Select(audit => audit.RunId).Distinct());
        Assert.Single(retryAudits.Select(audit => audit.TraceId).Distinct());

        var userAudits = audits
            .Where(audit =>
                audit.Action == "space.floor.publish")
            .ToList();
        Assert.Equal(2, userAudits.Count);
        Assert.All(userAudits, audit => Assert.Null(audit.RunId));
        Assert.DoesNotContain(
            retryAudits[0].TraceId,
            userAudits.Select(audit => audit.TraceId));
    }

    private static ServiceProvider BuildProvider(
        DbContextOptions<CP6Context> options,
        FailOnceWmsLocationConsumer consumer)
    {
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddScoped<ITenantContext, TenantContext>();
        services.AddScoped<ICurrentUserAccessor, TestCurrentUserAccessor>();
        services.AddScoped(sp => new CP6Context(
            sp.GetRequiredService<DbContextOptions<CP6Context>>(),
            sp.GetRequiredService<ITenantContext>(),
            sp.GetRequiredService<ICurrentUserAccessor>()));
        services.AddScoped<ITenantEnumerator, TenantEnumerator>();

        services.AddScoped<SpaceExecutionContextAccessor>();
        services.AddScoped<ISpaceExecutionContextAccessor>(
            sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<ISpaceExecutionContextManager>(
            sp => sp.GetRequiredService<SpaceExecutionContextAccessor>());
        services.AddScoped<
            ISpaceAuditDbContextFactory,
            SpaceAuditDbContextFactory>();
        services.AddScoped<ISpaceAuditWriter, SpaceAuditWriter>();
        services.AddScoped<ISpaceRetryFinalizer, SpaceRetryFinalizer>();

        services.AddSingleton<IWmsLocationConsumer>(consumer);
        services.AddScoped<ISpaceBridgeHook, SpaceBridgeHook>();
        services.AddScoped<IIntegrationEventDispatcher>(sp =>
            new IntegrationEventDispatcher(
                Mock.Of<IMesBridgeHook>(),
                Mock.Of<IWmsBridgeHook>(),
                Mock.Of<IErpBridgeHook>(),
                Mock.Of<IOrderCancelBridgeHook>(),
                Mock.Of<IFinBridgeHook>(),
                sp.GetRequiredService<ISpaceBridgeHook>()));

        services.AddScoped<ICodeEngineService, CodeEngineService>();
        services.AddScoped<IWmsStockQuery, StubWmsStockQuery>();
        services.AddScoped<IWmsBinDeactivator, WmsBinDeactivator>();
        services.AddScoped<ISpaceNotifier, NoOpSpaceNotifier>();
        services.AddScoped<ILocationPublishService, LocationPublishService>();

        services.AddScoped(_ => Mock.Of<IDeadLetterNotifier>());
        services.AddScoped(_ => Mock.Of<ISpaceDeadLetterNotifier>());
        services.AddLogging();
        return services.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
    }

    private static DefaultHttpContext NewAuthenticatedSpaceRequest(
        IServiceProvider services,
        Guid tenantId,
        Guid actorId,
        Guid correlationId)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim("tenant_id", tenantId.ToString()),
                new Claim(
                    ClaimTypes.NameIdentifier,
                    actorId.ToString()),
                new Claim(ClaimTypes.Name, "alice"),
                new Claim("subject_type", "internal"),
            ],
            authenticationType: "Test");
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = new ClaimsPrincipal(identity),
        };
        context.Request.Path = "/api/space/floors/publish";
        context.Request.Headers["X-Correlation-ID"] =
            correlationId.ToString();
        return context;
    }

    private static SpaceAuditEventInput PublishAudit(
        Guid floorId,
        string outcome,
        int? itemCount = null)
        => new(
            Action: "space.floor.publish",
            ResourceType: "Floor",
            ResourceId: floorId.ToString(),
            Outcome: outcome,
            FloorId: floorId,
            Evidence: new SpaceAuditEvidence(
                ItemCount: itemCount),
            ClientType: "Http");

    private static Guid SeedPublishableFloor(CP6Context db)
    {
        db.Sys_Tenants.Add(new Sys_Tenant
        {
            Id = TenantA,
            TenantCode = "TENANT-A",
            TenantName = "Tenant A",
            Enable = true,
        });
        var floorId = Guid.NewGuid();
        var site = new Space_Site
        {
            Id = Guid.NewGuid(),
            SiteCode = "S1",
            SiteName = "Site 1",
        };
        var floor = new Space_Floor
        {
            Id = floorId,
            SiteId = site.Id,
            Level = 1,
            FloorCode = "F1",
            FloorName = "Floor 1",
        };
        var zone = new Space_Zone
        {
            Id = Guid.NewGuid(),
            FloorId = floorId,
            ZoneCode = "Z1",
            ZoneName = "Zone 1",
        };
        var rack = new Space_Rack
        {
            Id = Guid.NewGuid(),
            ZoneId = zone.Id,
            FloorId = floorId,
            RackCode = "R1",
            Cols = 1,
            Levels = 1,
            CellW = 1000,
            CellH = 1000,
            CellD = 1000,
        };
        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(),
            RuleName = "default",
            ScopeType = 0,
            IsDefault = true,
            Segments = """
                [
                  {"Key":"zone","Source":"zone-code","Sep":"-"},
                  {"Key":"col","Source":"col","Sep":""}
                ]
                """,
        });
        db.AddRange(site, floor, zone, rack);
        db.Space_Locations.Add(new Space_Location
        {
            Id = Guid.NewGuid(),
            FloorId = floorId,
            RackId = rack.Id,
            Placed = true,
            Status = 0,
            CodeOrigin = 1,
            LocationCode = "Z1-1",
            Col = 1,
            Level = 1,
            Depth = 1,
        });
        db.SaveChanges();
        return floorId;
    }

    private sealed class FailOnceWmsLocationConsumer :
        IWmsLocationConsumer
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public List<SpaceRetryFence?> RetryFences { get; } = [];

        public Task<WmsConsumeResult> ConsumeAsync(
            LocationPublishBatch batch,
            SpaceRetryFence? retryFence = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            RetryFences.Add(retryFence);
            var call = Interlocked.Increment(ref _calls);
            return Task.FromResult(new WmsConsumeResult
            {
                Success = call > 1,
                Items = batch.Items.Select(item => new WmsItemResult
                {
                    LocationId = item.LocationId,
                    Status = call == 1 ? "REJECTED" : "UPSERTED",
                }).ToList(),
            });
        }
    }

    private sealed class TestCurrentUserAccessor :
        ICurrentUserAccessor
    {
        public Guid? UserId => ActorId;
        public string? UserName => "alice";
    }
}
