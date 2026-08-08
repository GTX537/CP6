using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.UnitTests;

public sealed class SpaceExcelCadMatchArtifactTests
{
    private static readonly Guid CadSourceId =
        Guid.Parse("10101010-2020-3030-4040-505050505050");
    private static readonly Guid CadParseJobId =
        Guid.Parse("20202020-3030-4040-5050-606060606060");
    private static readonly Guid MatchJobId =
        Guid.Parse("30303030-4040-5050-6060-707070707070");
    private static readonly Guid RequestedBy =
        Guid.Parse("40404040-5050-6060-7070-808080808080");
    private static readonly DateTime RequestedAtUtc =
        new(2026, 8, 8, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PreviewSet_is_deterministic_and_rejects_identity_tampering()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-001")]);
        var first = SpaceCadPreviewSet.Create(
            context.Scenario.Request.TenantId,
            context.Editor.ModelVersionId,
            CadSourceId,
            CadParseJobId,
            context.Semantic,
            context.Diagnostics);
        var second = SpaceCadPreviewSet.Create(
            context.Scenario.Request.TenantId,
            context.Editor.ModelVersionId,
            CadSourceId,
            CadParseJobId,
            context.Semantic,
            context.Diagnostics);

        var json = SpaceCadPreviewSet.Serialize(first);

        Assert.Equal(first.PreviewSetSha256, second.PreviewSetSha256);
        Assert.Equal(
            json,
            SpaceCadPreviewSet.Serialize(SpaceCadPreviewSet.Deserialize(json)));
        Assert.Matches("^[0-9a-f]{64}$", first.PreviewSetSha256);
        Assert.Throws<InvalidDataException>(() => SpaceCadPreviewSet.Validate(
            first with { CadParseJobId = Guid.NewGuid() }));
        Assert.Throws<InvalidDataException>(() => SpaceCadPreviewSet.Validate(
            first with { PreviewSetSha256 = new string('0', 64) }));
    }

    [Fact]
    public void MatchArtifact_seals_full_server_authority_and_rejects_tampering()
    {
        var context = SpaceExcelCadMatchingTests.Context(
            [SpaceExcelCadMatchingTests.Rack("R-001")]);
        var preview = SpaceExcelCadMatchingTests.Build(context);
        var payload = new SpaceExcelCadMatchJobPayload(
            SpaceExcelCadMatchArtifactVersions.SchemaVersion,
            preview.ModelVersionId,
            preview.ExcelSourceId,
            preview.PreflightJobId,
            CadSourceId,
            CadParseJobId,
            preview.FloorLogicalId,
            preview.EditorContentRevision);

        var first = SpaceExcelCadMatchArtifact.Create(
            context.Scenario.Request.TenantId,
            MatchJobId,
            payload,
            Guid.Parse("50505050-6060-7070-8080-909090909090"),
            RequestedBy,
            RequestedAtUtc,
            preview);
        var second = SpaceExcelCadMatchArtifact.Create(
            context.Scenario.Request.TenantId,
            MatchJobId,
            payload,
            first.CadPreviewSetArtifactId,
            RequestedBy,
            RequestedAtUtc,
            preview);
        var json = SpaceExcelCadMatchArtifact.Serialize(first);

        Assert.True(first.IsAuthoritativeArtifact);
        Assert.Equal(first.ArtifactPayloadSha256, second.ArtifactPayloadSha256);
        Assert.Equal(
            json,
            SpaceExcelCadMatchArtifact.Serialize(
                SpaceExcelCadMatchArtifact.Deserialize(json)));
        Assert.Matches("^[0-9a-f]{64}$", first.ArtifactPayloadSha256);
        Assert.Throws<InvalidDataException>(() =>
            SpaceExcelCadMatchArtifact.Validate(
                first with { ExpectedContentRevision = 99 }));
        Assert.Throws<InvalidDataException>(() =>
            SpaceExcelCadMatchArtifact.Validate(
                first with { CadPreviewSetArtifactId = Guid.NewGuid() }));
    }
}
