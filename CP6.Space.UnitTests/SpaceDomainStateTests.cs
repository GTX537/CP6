using CP6.Space.Application;
using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpaceDomainStateTests
{
    private static readonly Guid TenantId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly Guid SiteId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid ActorId =
        Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private const string ContentHash =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private const string WmsHash =
        "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    [Fact]
    public void Coordinator_creates_and_reserves_a_draft()
    {
        var model = SpaceModel.Create(TenantId, SiteId);
        var coordinator = new SpaceModelVersionCoordinator(
            new TestExecutionContext(TenantId, ActorId));

        var version = coordinator.CreateDraft(model, 1, "  First draft  ");

        Assert.Equal("First draft", version.Name);
        Assert.Equal(SpaceVersionStatus.Draft, version.Status);
        Assert.Equal(version.Id, model.ActiveDraftVersionId);
        Assert.Equal(model.Id, version.ModelId);
        Assert.Equal(TenantId, version.TenantId);
    }

    [Fact]
    public void Model_allows_only_one_active_draft()
    {
        var model = SpaceModel.Create(TenantId, SiteId);
        var first = SpaceModelVersion.CreateDraft(TenantId, model.Id, 1, "First");
        var second = SpaceModelVersion.CreateDraft(TenantId, model.Id, 2, "Second");

        model.ReserveDraft(first);

        Assert.Throws<SpaceVersionConflictException>(() => model.ReserveDraft(second));
    }

    [Fact]
    public void Model_rejects_cross_tenant_and_cross_model_drafts()
    {
        var model = SpaceModel.Create(TenantId, SiteId);
        var otherTenant = SpaceModelVersion.CreateDraft(
            Guid.NewGuid(), model.Id, 1, "Other tenant");
        var otherModel = SpaceModelVersion.CreateDraft(
            TenantId, Guid.NewGuid(), 1, "Other model");

        Assert.Throws<SpaceTenantScopeException>(() => model.ReserveDraft(otherTenant));
        Assert.Throws<SpaceVersionConflictException>(() => model.ReserveDraft(otherModel));
    }

    [Fact]
    public void Version_happy_path_reaches_published_then_superseded()
    {
        var version = NewDraft();
        var publishedAt = new DateTime(2026, 7, 26, 12, 0, 0, DateTimeKind.Utc);

        version.BeginValidation();
        version.MarkReady(ContentHash, "space-v1", WmsHash);
        version.BeginPublishing();
        version.MarkPublished(ActorId, publishedAt);

        Assert.Equal(SpaceVersionStatus.Published, version.Status);
        Assert.Equal(ActorId, version.PublishedBy);
        Assert.Equal(publishedAt, version.PublishedAtUtc);

        version.MarkSuperseded();
        Assert.Equal(SpaceVersionStatus.Superseded, version.Status);
    }

    [Fact]
    public void Editing_ready_content_invalidates_all_validation_evidence()
    {
        var version = NewReady();

        version.TouchContent();

        Assert.Equal(SpaceVersionStatus.Draft, version.Status);
        Assert.Equal(1, version.ContentRevision);
        Assert.Null(version.ContentHash);
        Assert.Null(version.ValidatedHash);
        Assert.Null(version.RuleSetVersion);
        Assert.Null(version.WmsCapabilityHash);
    }

    [Fact]
    public void Publishing_can_retry_only_before_external_commit()
    {
        var version = NewReady();
        version.BeginPublishing();

        version.ReturnToReadyBeforeExternalCommit();

        Assert.Equal(SpaceVersionStatus.Ready, version.Status);
        version.BeginPublishing();
        version.MarkReconciliationRequired();
        Assert.Equal(SpaceVersionStatus.ReconciliationRequired, version.Status);

        version.ResumePublishingAfterReconciliation();
        Assert.Equal(SpaceVersionStatus.Publishing, version.Status);
    }

    [Fact]
    public void Published_history_is_not_editable()
    {
        var version = NewReady();
        version.BeginPublishing();
        version.MarkPublished(ActorId, DateTime.UtcNow);

        Assert.Throws<SpaceVersionStateException>(() => version.TouchContent());
        Assert.Throws<SpaceVersionStateException>(() => version.Rename("Changed"));
        Assert.Throws<SpaceVersionStateException>(() => version.BeginValidation());
    }

    [Fact]
    public void Invalid_state_transitions_fail_closed()
    {
        var draft = NewDraft();
        Assert.Throws<SpaceVersionStateException>(() =>
            draft.MarkReady(ContentHash, "space-v1", WmsHash));
        Assert.Throws<SpaceVersionStateException>(() => draft.BeginPublishing());
        Assert.Throws<SpaceVersionStateException>(() => draft.MarkReconciliationRequired());
        Assert.Throws<SpaceVersionStateException>(() =>
            draft.MarkPublished(ActorId, DateTime.UtcNow));
        Assert.Throws<SpaceVersionStateException>(() => draft.MarkSuperseded());
    }

    [Fact]
    public void Cutover_is_one_way_after_design_activation()
    {
        var model = SpaceModel.Create(TenantId, SiteId);
        var bootstrap = NewDraft(model.Id);
        bootstrap.BeginValidation();
        bootstrap.MarkReady(ContentHash, "space-v1", WmsHash);
        bootstrap.BeginPublishing();
        bootstrap.MarkPublished(ActorId, DateTime.UtcNow);

        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.MarkBootstrapping();
        model.MarkVerified(bootstrap);
        model.ActivateDesignV1();

        Assert.Equal(SpaceModelMode.DesignV1, model.Mode);
        Assert.Equal(SpaceModelCutoverState.DesignV1, model.CutoverState);
        Assert.Throws<SpaceVersionStateException>(() =>
            model.ReopenLegacy(approved: true, designWritesAccepted: false));
    }

    [Fact]
    public void Failed_legacy_cutover_requires_approval_and_no_design_writes()
    {
        var model = SpaceModel.Create(TenantId, SiteId);
        model.BeginCutover(Guid.NewGuid());
        model.MarkFrozen();
        model.FailCutover();

        Assert.Throws<SpaceVersionStateException>(() =>
            model.ReopenLegacy(approved: false, designWritesAccepted: false));
        Assert.Throws<SpaceVersionStateException>(() =>
            model.ReopenLegacy(approved: true, designWritesAccepted: true));

        model.ReopenLegacy(approved: true, designWritesAccepted: false);
        Assert.Equal(SpaceModelCutoverState.LegacyOpen, model.CutoverState);
    }

    [Fact]
    public void Coordinator_rejects_an_unverified_tenant()
    {
        var model = SpaceModel.Create(TenantId, SiteId);
        var coordinator = new SpaceModelVersionCoordinator(
            new TestExecutionContext(Guid.NewGuid(), ActorId));

        Assert.Throws<SpaceTenantScopeException>(() =>
            coordinator.CreateDraft(model, 1, "Forbidden"));
    }

    private static SpaceModelVersion NewDraft(Guid? modelId = null) =>
        SpaceModelVersion.CreateDraft(
            TenantId,
            modelId ?? Guid.NewGuid(),
            1,
            "Draft");

    private static SpaceModelVersion NewReady()
    {
        var version = NewDraft();
        version.BeginValidation();
        version.MarkReady(ContentHash, "space-v1", WmsHash);
        return version;
    }

    private sealed record TestExecutionContext(Guid TenantId, Guid ActorId)
        : ISpaceExecutionContext;
}
