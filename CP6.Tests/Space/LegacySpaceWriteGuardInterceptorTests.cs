using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Compatibility;
using CP6.Entity.DomainModels.Space;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace CP6.Tests.Space;

public class LegacySpaceWriteGuardInterceptorTests
{
    private static readonly Guid TenantId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid DesignSiteId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid LegacySiteId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    [Fact]
    public async Task DesignSite_BlocksLegacyWrite_ButKeepsLegacyReadCompatible()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();

        var site = await db.Space_Sites.SingleAsync(x => x.Id == DesignSiteId);
        Assert.Equal("DESIGN", site.SiteCode);

        site.SiteName = "blocked update";
        var error = await Assert.ThrowsAsync<BizException>(() => db.SaveChangesAsync());

        Assert.Equal(SpaceCompatibilityErrors.LegacyWriteDisabled, error.Code);
        Assert.Equal(409, error.HttpStatus);
    }

    [Fact]
    public async Task LegacySite_WriteRemainsAllowed()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();

        var site = await db.Space_Sites.SingleAsync(x => x.Id == LegacySiteId);
        site.SiteName = "allowed update";

        await db.SaveChangesAsync();

        Assert.Equal("allowed update", site.SiteName);
    }

    [Fact]
    public async Task ChildEntity_ResolvesSiteAndBlocksWrite()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();

        var floor = await db.Space_Floors.SingleAsync(x => x.SiteId == DesignSiteId);
        floor.FloorName = "blocked floor update";
        var error = await Assert.ThrowsAsync<BizException>(() => db.SaveChangesAsync());

        Assert.Equal(SpaceCompatibilityErrors.LegacyWriteDisabled, error.Code);
    }

    [Fact]
    public async Task TenantWideLegacyEntity_IsBlockedWhenAnySiteUsesDesignV1()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();

        db.Space_CodeRules.Add(new Space_CodeRule
        {
            Id = Guid.NewGuid(),
            RuleName = "tenant default",
            Segments = "[]",
        });
        var error = await Assert.ThrowsAsync<BizException>(() => db.SaveChangesAsync());

        Assert.Equal(SpaceCompatibilityErrors.LegacyWriteDisabled, error.Code);
    }

    [Fact]
    public async Task CrossTenantLegacyEntity_IsDeniedBeforePersistence()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();
        var otherTenantSite = new Space_Site
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SiteCode = "OTHER",
            SiteName = "Other tenant",
        };
        db.Space_Sites.Attach(otherTenantSite);
        db.Entry(otherTenantSite).State = EntityState.Modified;

        var error = await Assert.ThrowsAsync<BizException>(() => db.SaveChangesAsync());

        Assert.Equal(SpaceCompatibilityErrors.TenantScopeDenied, error.Code);
        Assert.Equal(403, error.HttpStatus);
    }

    [Fact]
    public async Task DesignV1Tenant_AllowsAuditAppend_ButAuditRemainsAppendOnly()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();
        var audit = new Space_AuditEvent
        {
            OccurredAtUtc = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc),
            ActorType = "User",
            ActorId = "user-1",
            Action = "space.floor.publish",
            ResourceType = "Floor",
            ResourceId = Guid.NewGuid().ToString(),
            Outcome = "Started",
            CorrelationId = Guid.NewGuid(),
            TraceId = "0123456789abcdef0123456789abcdef",
        };
        db.SpaceAuditEvents.Add(audit);

        await db.SaveChangesAsync();

        Assert.Equal(TenantId, audit.TenantId);
        audit.Outcome = "Succeeded";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());
        Assert.Equal("SPACE_AUDIT_APPEND_ONLY", error.Message);
    }

    [Fact]
    public async Task DesignV1Tenant_AllowsOperationalAnalyticsWrites()
    {
        var fixture = await CreateFixtureAsync();
        await using (var db = fixture.GuardedContext())
        {
            db.Space_AnalyticsConfigs.Add(new Space_AnalyticsConfig());
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.GuardedContext())
        {
            db.Space_AbcSnapshots.Add(new Space_AbcSnapshot
            {
                Id = Guid.NewGuid(),
                SiteId = DesignSiteId,
                WarehouseCd = "DESIGN",
                WindowFrom = DateTime.UtcNow.AddDays(-90),
                WindowTo = DateTime.UtcNow,
                CalculatedAt = DateTime.UtcNow,
                ThresholdA = 0.80m,
                ThresholdB = 0.95m,
                Trigger = "manual",
            });
            await db.SaveChangesAsync();
        }

        await using var verify = fixture.GuardedContext();
        Assert.Single(await verify.Space_AnalyticsConfigs.ToListAsync());
        Assert.Single(await verify.Space_AbcSnapshots.ToListAsync());
    }

    [Fact]
    public async Task CrossTenantOperationalAnalyticsWrite_RemainsDenied()
    {
        var fixture = await CreateFixtureAsync();
        await using var db = fixture.GuardedContext();
        var snapshot = new Space_AbcSnapshot
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SiteId = DesignSiteId,
            WarehouseCd = "DESIGN",
            Trigger = "manual",
        };
        db.Space_AbcSnapshots.Attach(snapshot);
        db.Entry(snapshot).State = EntityState.Modified;

        var error = await Assert.ThrowsAsync<BizException>(() => db.SaveChangesAsync());

        Assert.Equal(SpaceCompatibilityErrors.TenantScopeDenied, error.Code);
        Assert.Equal(403, error.HttpStatus);
    }

    private static async Task<Fixture> CreateFixtureAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        var root = new InMemoryDatabaseRoot();
        var tenant = new TenantContext { CurrentTenantId = TenantId };
        var plainOptions = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(databaseName, root)
            .Options;

        await using (var seed = new CP6Context(plainOptions, tenant))
        {
            seed.Space_Sites.AddRange(
                new Space_Site
                {
                    Id = DesignSiteId,
                    SiteCode = "DESIGN",
                    SiteName = "Design Site",
                },
                new Space_Site
                {
                    Id = LegacySiteId,
                    SiteCode = "LEGACY",
                    SiteName = "Legacy Site",
                });
            seed.Space_Floors.Add(new Space_Floor
            {
                Id = Guid.NewGuid(),
                SiteId = DesignSiteId,
                FloorCode = "F1",
                FloorName = "Floor 1",
            });
            await seed.SaveChangesAsync();
        }

        return new Fixture(databaseName, root, tenant);
    }

    private sealed record Fixture(
        string DatabaseName,
        InMemoryDatabaseRoot Root,
        TenantContext Tenant)
    {
        public CP6Context GuardedContext()
        {
            var gate = new SpaceCompatibilityGate(
                Tenant,
                Options.Create(new SpaceCompatibilityOptions
                {
                    DesignApiEnabled = true,
                    Sites =
                    [
                        new SpaceSiteCompatibilityOptions
                        {
                            TenantId = TenantId,
                            SiteId = DesignSiteId,
                            Mode = SpaceSiteMode.DesignV1,
                            CutoverState = SpaceCutoverState.DesignV1,
                            Evidence = new SpaceCutoverEvidence
                            {
                                BootstrapVerified = true,
                                RuntimeHashVerified = true,
                                WmsIdentityVerified = true,
                                DesignWritesAccepted = true,
                            },
                        },
                    ],
                }));
            var interceptor = new LegacySpaceWriteGuardInterceptor(Tenant, gate);
            var options = new DbContextOptionsBuilder<CP6Context>()
                .UseInMemoryDatabase(DatabaseName, Root)
                .AddInterceptors(interceptor)
                .Options;
            return new CP6Context(options, Tenant);
        }
    }
}
