using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceVersionCloneTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid ActorId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Initializing_clone_reserves_slot_and_becomes_editable_after_completion()
    {
        var model = SpaceModel.Create(TenantId, Guid.NewGuid());
        var sourceId = Guid.NewGuid();
        var clone = SpaceModelVersion.CreateInitializingClone(
            TenantId,
            model.Id,
            2,
            "Warehouse draft",
            sourceId,
            Guid.NewGuid());

        model.ReserveDraft(clone);

        Assert.Equal(clone.Id, model.ActiveDraftVersionId);
        Assert.Equal(SpaceVersionStatus.Initializing, clone.Status);
        Assert.Throws<SpaceVersionStateException>(() => clone.TouchContent());

        clone.CompleteInitialization(42);

        Assert.Equal(SpaceVersionStatus.Draft, clone.Status);
        Assert.Equal(42, clone.ContentRevision);
        Assert.Equal(sourceId, clone.BasedOnVersionId);
        clone.TouchContent();
        Assert.Equal(43, clone.ContentRevision);
    }

    [Fact]
    public void Failed_clone_releases_only_its_own_reservation()
    {
        var model = SpaceModel.Create(TenantId, Guid.NewGuid());
        var clone = SpaceModelVersion.CreateInitializingClone(
            TenantId,
            model.Id,
            2,
            "Draft",
            Guid.NewGuid(),
            Guid.NewGuid());
        model.ReserveDraft(clone);

        clone.FailInitialization();
        model.ReleaseFailedClone(clone);

        Assert.Equal(SpaceVersionStatus.Failed, clone.Status);
        Assert.Null(model.ActiveDraftVersionId);
        Assert.Throws<SpaceVersionConflictException>(() =>
            model.ReleaseDraft(clone.Id));
    }

    [Fact]
    public void Revision_source_must_share_tenant_and_version()
    {
        var versionId = Guid.NewGuid();
        var floor = SpaceFloorRevision.Create(
            TenantId,
            versionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "F1",
            "Floor 1");
        var source = SpaceModelSource.CreateInlineSource(
            TenantId,
            versionId,
            SpaceSourceType.Editor,
            "Editor",
            new string('a', 64));
        var otherVersionSource = SpaceModelSource.CreateInlineSource(
            TenantId,
            Guid.NewGuid(),
            SpaceSourceType.Editor,
            "Other",
            new string('b', 64));

        floor.AttachSource(source, "editor:floor-1");

        Assert.Equal(source.Id, floor.SourceId);
        Assert.Throws<SpaceVersionConflictException>(() =>
            floor.AttachSource(otherVersionSource, null));
    }

    [Fact]
    public void Revision_geometry_uses_integer_millimeters_and_normalized_rotation()
    {
        var rack = SpaceRackRevision.Create(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "R01");

        rack.ConfigureGeometry(100, 200, 0, -90m, 1200, 1000, 5000);

        Assert.Equal(270m, rack.RotationZ);
        Assert.Equal(1200, rack.Width);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            rack.ConfigureGeometry(0, 0, 0, 0, 0, 100, 100));
    }

    [Fact]
    public void Element_attribute_rejects_cross_tenant_binding()
    {
        var element = SpaceElementRevision.Create(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Column",
            """{"kind":"box"}""");

        Assert.Throws<SpaceTenantScopeException>(() =>
            SpaceElementAttribute.Create(
                Guid.NewGuid(),
                element,
                "design",
                "fireRating",
                "String",
                "2h"));
    }

    [Fact]
    public async Task Coordinator_requires_verified_execution_context()
    {
        var store = new RecordingCloneStore();
        var coordinator = new SpaceVersionCloneCoordinator(
            new TestExecutionContext(Guid.Empty, ActorId),
            store);

        await Assert.ThrowsAsync<SpaceTenantScopeException>(() =>
            coordinator.StartAsync(
                new SpaceVersionCloneRequest(
                    Guid.NewGuid(),
                    "Draft",
                    Guid.NewGuid())));
        Assert.Equal(0, store.CallCount);
    }

    private sealed class RecordingCloneStore : ISpaceVersionCloneStore
    {
        public int CallCount { get; private set; }

        public Task<SpaceVersionCloneStartResult> StartAsync(
            SpaceVersionCloneRequest request,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(
                new SpaceVersionCloneStartResult(
                    Guid.NewGuid(),
                    2,
                    SpaceVersionStatus.Initializing,
                    Guid.NewGuid(),
                    SpaceJobStatus.Queued,
                    false));
        }
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;
}
