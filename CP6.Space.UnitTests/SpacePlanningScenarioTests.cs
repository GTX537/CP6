using CP6.Space.Domain;

namespace CP6.Space.UnitTests;

public sealed class SpacePlanningScenarioTests
{
    private static readonly Guid TenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Scenario_version_is_editable_but_cannot_enter_publish_lifecycle()
    {
        var version =
            SpaceModelVersion.CreateInitializingPlanningScenario(
                TenantId,
                Guid.NewGuid(),
                2,
                "Peak season",
                Guid.NewGuid(),
                Guid.NewGuid());

        version.CompleteInitialization(7);
        version.TouchContent();
        version.BeginValidation();
        version.MarkReady(
            new string('a', 64),
            "space-rules-v1",
            new string('b', 64));

        var error = Assert.Throws<SpaceVersionStateException>(
            version.BeginPublishing);

        Assert.Equal(
            SpaceModelVersionPurpose.PlanningScenario,
            version.Purpose);
        Assert.Equal(SpaceVersionStatus.Ready, version.Status);
        Assert.Contains(
            "production publish lifecycle",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Scenario_version_cannot_occupy_production_model_pointers()
    {
        var model = SpaceModel.Create(TenantId, Guid.NewGuid());
        var version =
            SpaceModelVersion.CreateInitializingPlanningScenario(
                TenantId,
                model.Id,
                2,
                "Capacity option",
                Guid.NewGuid(),
                Guid.NewGuid());

        Assert.Throws<SpaceVersionStateException>(() =>
            model.ReserveDraft(version));

        version.CompleteInitialization(0);
        version.BeginValidation();
        version.MarkReady(
            new string('a', 64),
            "space-rules-v1",
            new string('b', 64));
        Assert.Throws<SpaceVersionStateException>(() =>
            model.SetPublishedVersion(version, new string('c', 64)));

        Assert.Null(model.ActiveDraftVersionId);
        Assert.Null(model.CurrentPublishedVersionId);
    }

    [Fact]
    public void Branch_requires_distinct_versions_and_sha256_request_identity()
    {
        var versionId = Guid.NewGuid();
        var common = new SpacePlanningScenarioBranchData(
            Guid.NewGuid(),
            Guid.NewGuid(),
            versionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Option A",
            "space-planning-scenario-v1",
            new string('a', 64));
        var branch = SpacePlanningScenarioBranch.Create(
            TenantId,
            Guid.NewGuid(),
            common);

        Assert.Equal("Option A", branch.Name);
        Assert.Throws<ArgumentException>(() =>
            SpacePlanningScenarioBranch.Create(
                TenantId,
                Guid.NewGuid(),
                common with { ScenarioVersionId = versionId }));
        Assert.Throws<ArgumentException>(() =>
            SpacePlanningScenarioBranch.Create(
                TenantId,
                Guid.NewGuid(),
                common with { RequestHash = "not-a-hash" }));
    }
}
