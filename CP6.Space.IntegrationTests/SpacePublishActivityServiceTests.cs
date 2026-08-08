using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using CP6.Space.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.IntegrationTests;

public sealed class SpacePublishActivityServiceTests
{
    private static readonly DateTime Now =
        new(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Lists_newest_attempts_with_job_state_and_protected_cursor()
    {
        await using var fixture = await Fixture.CreateAsync();
        var older = fixture.AddAttempt(Now.AddMinutes(-5), "Older layout");
        var newest = fixture.AddAttempt(Now, "Latest layout");
        await fixture.Context.SaveChangesAsync();

        var first = await fixture.Service.GetBySiteAsync(
            fixture.SiteId,
            null,
            1,
            null);

        var item = Assert.Single(first.Items);
        Assert.Equal(newest.Id, item.Id);
        Assert.Equal("Latest layout", item.TargetVersionName);
        Assert.Equal("Requested", item.Status);
        Assert.Equal("Queued", item.JobStatus);
        Assert.NotNull(first.NextCursor);

        var second = await fixture.Service.GetBySiteAsync(
            fixture.SiteId,
            null,
            1,
            first.NextCursor);
        Assert.Equal(older.Id, Assert.Single(second.Items).Id);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Rejects_unknown_status_and_external_principal()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invalid = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            fixture.Service.GetBySiteAsync(
                fixture.SiteId,
                "NotAStatus",
                20,
                null));
        Assert.Equal(400, invalid.StatusCode);

        await using var external = await Fixture.CreateAsync(isExternal: true);
        var denied = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            external.Service.GetBySiteAsync(
                external.SiteId,
                null,
                20,
                null));
        Assert.Equal(SpaceErrorCodes.ExternalSubjectDenied, denied.Code);
    }

    private sealed record Fixture(
        SpaceContext Context,
        SpacePublishActivityService Service,
        TestExecutionContext Execution,
        Guid SiteId,
        SpaceModel Model) : IAsyncDisposable
    {
        public static async Task<Fixture> CreateAsync(bool isExternal = false)
        {
            var execution = new TestExecutionContext(
                Guid.NewGuid(),
                Guid.NewGuid(),
                isExternal);
            var context = new SpaceContext(
                new DbContextOptionsBuilder<SpaceContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString("N"),
                        SpaceTestDatabaseRoots.InMemory)
                    .Options,
                execution,
                new FixedClock());
            var siteId = Guid.NewGuid();
            var model = SpaceModel.Create(execution.TenantId, siteId);
            context.Models.Add(model);
            await context.SaveChangesAsync();
            var service = new SpacePublishActivityService(
                context,
                execution,
                new AllowAccess(),
                new TestCursorCodec());
            return new Fixture(context, service, execution, siteId, model);
        }

        public SpacePublishAttempt AddAttempt(DateTime startedAtUtc, string name)
        {
            var version = SpaceModelVersion.CreateDraft(
                Execution.TenantId,
                Model.Id,
                Context.Versions.Local.Count + 1,
                name);
            var job = SpaceJob.CreateQueued(
                Execution.TenantId,
                SpaceJobType.Publish,
                SpaceJobSubjectType.PublishAttempt,
                Guid.NewGuid(),
                new string('b', 64),
                new string('c', 64),
                50,
                5,
                Execution.ActorId,
                startedAtUtc,
                Guid.NewGuid());
            var attempt = SpacePublishAttempt.Create(
                Execution.TenantId,
                SiteId,
                Guid.NewGuid(),
                version.Id,
                null,
                "cp6-wms-v1",
                $"key-{Guid.NewGuid():N}",
                new string('a', 64),
                Execution.ActorId,
                null,
                null,
                "{}",
                startedAtUtc,
                Guid.NewGuid());
            attempt.BindInitialJob(job.Id);
            Context.AddRange(version, job, attempt);
            return attempt;
        }

        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed record TestExecutionContext(
        Guid TenantId,
        Guid ActorId,
        bool IsExternal) : ISpaceExecutionContext;

    private sealed class FixedClock : ISpaceClock
    {
        public DateTime UtcNow => Now;
    }

    private sealed class AllowAccess : ISpaceDesignAccessEvaluator
    {
        public void EnsureSiteAccess(Guid siteId, bool write)
        {
        }
    }

    private sealed class TestCursorCodec : ISpaceCursorCodec
    {
        public string Encode(SpaceCursorState state) =>
            Convert.ToBase64String(
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(state)));

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
        {
            var state = JsonSerializer.Deserialize<SpaceCursorState>(
                Encoding.UTF8.GetString(Convert.FromBase64String(cursor)))!;
            if (state.Resource != expectedResource ||
                state.FilterHash != expectedFilterHash)
                throw new FormatException();
            return state;
        }
    }
}
