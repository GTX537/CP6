using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePlanningExchangeServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 2, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Export_is_deterministic_valid_glb_with_canonical_coordinates()
    {
        await using var fixture = await Fixture.CreateAsync();

        var first = await fixture.Service.ExportGlbAsync(
            fixture.SiteId,
            fixture.BranchId);
        var second = await fixture.Service.ExportGlbAsync(
            fixture.SiteId,
            fixture.BranchId);
        using var parsed = Parse(first.Content);
        var root = parsed.RootElement;

        Assert.Equal(first.Content, second.Content);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal("model/gltf-binary", first.ContentType);
        Assert.EndsWith(".glb", first.FileName, StringComparison.Ordinal);
        Assert.Equal("2.0", root.GetProperty("asset").GetProperty("version").GetString());
        Assert.Equal(3, root.GetProperty("accessors").GetArrayLength());
        Assert.Equal(3, root.GetProperty("bufferViews").GetArrayLength());
        var primitive = root.GetProperty("meshes")[0]
            .GetProperty("primitives")[0];
        Assert.Equal(
            1,
            primitive.GetProperty("attributes").GetProperty("NORMAL").GetInt32());
        Assert.Equal(2, primitive.GetProperty("indices").GetInt32());
        var cp6 = root.GetProperty("asset").GetProperty("extras").GetProperty("cp6");
        Assert.Equal(
            SpacePlanningExchangeService.SchemaVersion,
            cp6.GetProperty("schemaVersion").GetString());
        Assert.Equal("LOCAL_MM_Z_UP", cp6.GetProperty("sourceCoordinateSystem").GetString());
        Assert.Equal("(x,y,z)_mm -> (x,z,-y)_m", cp6.GetProperty("coordinateTransform").GetString());
        Assert.True(cp6.GetProperty("productionIsolated").GetBoolean());
        Assert.False(cp6.GetProperty("productionWriteAllowed").GetBoolean());
        Assert.False(cp6.GetProperty("runtimeOverlayIncluded").GetBoolean());

        var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
        var floor = Find(nodes, "Floor:F1");
        AssertVector(floor.GetProperty("translation"), 0, 3, 0);
        var rack = Find(nodes, "Rack:R-01");
        AssertVector(rack.GetProperty("translation"), 0.5, 1.5, -3);
        AssertVector(rack.GetProperty("scale"), 2, 3, 1);
        AssertVector(
            rack.GetProperty("rotation"),
            0,
            Math.Sqrt(0.5),
            0,
            Math.Sqrt(0.5));
        var location = Find(nodes, "Location:L-01");
        AssertVector(location.GetProperty("translation"), 0.5, 0.8, -2.5);
        AssertVector(location.GetProperty("scale"), 1, 1.2, 1);
        Assert.Equal(
            fixture.LocationId.ToString("D"),
            location.GetProperty("extras").GetProperty("cp6")
                .GetProperty("logicalId").GetString());

        var json = Encoding.UTF8.GetString(first.Content);
        Assert.DoesNotContain("stockQuantity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("personnelToken", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deviceEvent", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("historicalTask", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            fixture.PublishedVersionId,
            (await fixture.Context.Models.SingleAsync())
                .CurrentPublishedVersionId);
    }

    [Fact]
    public async Task External_and_unready_branches_fail_closed()
    {
        await using var ready = await Fixture.CreateAsync();
        var external = ready.CreateService(external: true);
        var externalError = await Assert.ThrowsAsync<SpaceProblemException>(
            () => external.ExportGlbAsync(ready.SiteId, ready.BranchId));
        Assert.Equal(
            SpaceErrorCodes.PlanningScenarioInternalOnly,
            externalError.Code);

        await using var unready = await Fixture.CreateAsync(completeClone: false);
        var unreadyError = await Assert.ThrowsAsync<SpaceProblemException>(
            () => unready.Service.ExportGlbAsync(
                unready.SiteId,
                unready.BranchId));
        Assert.Equal(
            SpaceErrorCodes.PlanningExchangeUnavailable,
            unreadyError.Code);
    }

    private static JsonDocument Parse(byte[] content)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        Assert.Equal(0x46546C67u, reader.ReadUInt32());
        Assert.Equal(2u, reader.ReadUInt32());
        Assert.Equal((uint)content.Length, reader.ReadUInt32());
        var jsonLength = checked((int)reader.ReadUInt32());
        Assert.Equal(0x4E4F534Au, reader.ReadUInt32());
        Assert.Equal(0, jsonLength % 4);
        var json = reader.ReadBytes(jsonLength);
        var binaryLength = checked((int)reader.ReadUInt32());
        Assert.Equal(0x004E4942u, reader.ReadUInt32());
        Assert.Equal(0, binaryLength % 4);
        Assert.Equal(binaryLength, reader.ReadBytes(binaryLength).Length);
        Assert.Equal(content.Length, stream.Position);
        return JsonDocument.Parse(Encoding.UTF8.GetString(json).TrimEnd(' '));
    }

    private static JsonElement Find(
        IEnumerable<JsonElement> nodes,
        string name) =>
        Assert.Single(nodes, value =>
            value.GetProperty("name").GetString() == name);

    private static void AssertVector(
        JsonElement value,
        params double[] expected)
    {
        var actual = value.EnumerateArray().Select(item => item.GetDouble()).ToArray();
        Assert.Equal(expected.Length, actual.Length);
        for (var index = 0; index < expected.Length; index++)
            Assert.Equal(expected[index], actual[index], precision: 10);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            SpaceContext context,
            TestExecution execution,
            Guid siteId,
            Guid branchId,
            Guid locationId,
            Guid publishedVersionId)
        {
            Context = context;
            Execution = execution;
            SiteId = siteId;
            BranchId = branchId;
            LocationId = locationId;
            PublishedVersionId = publishedVersionId;
            Service = CreateService();
        }

        public SpaceContext Context { get; }
        public TestExecution Execution { get; }
        public Guid SiteId { get; }
        public Guid BranchId { get; }
        public Guid LocationId { get; }
        public Guid PublishedVersionId { get; }
        public SpacePlanningExchangeService Service { get; }

        public SpacePlanningExchangeService CreateService(
            bool external = false) => new(
                Context,
                Execution with { IsExternal = external },
                new RecordingAccess(SiteId));

        public static async Task<Fixture> CreateAsync(
            bool completeClone = true)
        {
            var execution = new TestExecution(Guid.NewGuid(), Guid.NewGuid());
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
                    .Options,
                execution,
                new TestClock());
            var siteId = Guid.NewGuid();
            var branchId = Guid.NewGuid();
            var model = SpaceModel.Create(execution.TenantId, siteId);
            var published = SpaceModelVersion.CreateDraft(
                execution.TenantId,
                model.Id,
                1,
                "Production");
            published.BeginValidation();
            published.MarkReady(
                new string('1', 64),
                "space-rules-v1",
                new string('2', 64));
            published.BeginPublishing();
            published.MarkPublished(execution.ActorId, Now);
            model.SetPublishedVersion(published, new string('3', 64));
            var scenario = SpaceModelVersion.CreateInitializingPlanningScenario(
                execution.TenantId,
                model.Id,
                2,
                "Option",
                published.Id,
                branchId);
            scenario.CompleteInitialization(7);
            var job = SpaceJob.CreateQueued(
                execution.TenantId,
                SpaceJobType.CloneVersion,
                SpaceJobSubjectType.ModelVersion,
                scenario.Id,
                new string('4', 64),
                new string('5', 64),
                50,
                3,
                execution.ActorId,
                Now,
                Guid.NewGuid());
            if (completeClone)
            {
                var attempt = job.Claim(
                    "planning-test",
                    "v1",
                    Now,
                    TimeSpan.FromMinutes(5));
                job.Complete(
                    attempt.Id,
                    "planning-test",
                    Now.AddSeconds(1));
            }
            var branch = SpacePlanningScenarioBranch.Create(
                execution.TenantId,
                branchId,
                new SpacePlanningScenarioBranchData(
                    siteId,
                    model.Id,
                    published.Id,
                    scenario.Id,
                    job.Id,
                    "Option",
                    "space-planning-scenario-v1",
                    new string('6', 64)));
            var floorId = Guid.NewGuid();
            var floor = SpaceFloorRevision.Create(
                execution.TenantId,
                scenario.Id,
                floorId,
                siteId,
                1,
                "F1",
                "First",
                elevation: 3000,
                height: 5000);
            var zone = SpaceZoneRevision.Create(
                execution.TenantId,
                scenario.Id,
                Guid.NewGuid(),
                floorId,
                "Z1",
                1);
            var rack = SpaceRackRevision.Create(
                execution.TenantId,
                scenario.Id,
                Guid.NewGuid(),
                floorId,
                zone.LogicalId,
                "R-01");
            rack.ConfigureGeometry(
                1000,
                2000,
                0,
                90,
                2000,
                1000,
                3000);
            var level = SpaceRackLevelRevision.Create(
                execution.TenantId,
                scenario.Id,
                Guid.NewGuid(),
                rack.LogicalId,
                1,
                100,
                1500,
                2,
                1,
                1000,
                1000,
                beamHeight: 100);
            var locationId = Guid.NewGuid();
            var location = SpaceLocationRevision.Create(
                execution.TenantId,
                scenario.Id,
                locationId,
                floorId,
                rack.LogicalId,
                "L-01",
                1,
                1,
                1,
                1000,
                1200,
                1000);
            var element = SpaceElementRevision.Create(
                execution.TenantId,
                scenario.Id,
                Guid.NewGuid(),
                floorId,
                SpaceElementTypes.Column,
                """
                {"schemaVersion":1,"kind":"box","width":400,"height":5000,"depth":400}
                """);
            element.ConfigurePlacement(4000, 5000, 0, 0, 400, 5000, 400);
            element.ConfigureBusinessLink("COLUMN-01", null, null);
            context.AddRange(
                model,
                published,
                scenario,
                job,
                branch,
                floor,
                zone,
                rack,
                level,
                location,
                element);
            await context.SaveChangesAsync();
            return new Fixture(
                context,
                execution,
                siteId,
                branchId,
                locationId,
                published.Id);
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecution(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext
    {
        public bool IsExternal { get; init; }
    }

    private sealed class TestClock : ISpaceClock
    {
        public DateTime UtcNow => Now.AddSeconds(2);
    }

    private sealed class RecordingAccess(Guid expectedSiteId)
        : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
            Assert.Equal(expectedSiteId, siteId);
            Assert.False(write);
        }
    }
}
