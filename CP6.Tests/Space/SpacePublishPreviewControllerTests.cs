using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpacePublishPreviewControllerTests
{
    [Fact]
    public async Task Get_forwards_filters_and_returns_authoritative_preview()
    {
        var versionId = Guid.NewGuid();
        var floorId = Guid.NewGuid();
        var preview = new SpacePublishPreviewDto(
            versionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Passed",
            0,
            new string('a', 64),
            SpacePublishPlanRuleSet.Version,
            "cp6-wms-v1",
            new string('b', 64),
            new string('c', 64),
            true,
            1,
            1,
            1,
            new SpacePublishChangeSummaryDto(1, 0, 0, 0, 0, 0),
            new SpacePublishImpactSummaryDto(1, 0, 0, 0, 0, 0, 0),
            [],
            null);
        var service = new Mock<ISpacePublishPreviewService>();
        service.Setup(value => value.GetPreviewAsync(
                versionId,
                floorId,
                SpacePublishObjectTypes.Location,
                SpacePublishActions.Create,
                SpacePublishImpactCodes.WmsCreateLocation,
                true,
                25,
                "cursor",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(preview);
        var controller = new SpacePublishPreviewController(service.Object);

        var result = await controller.GetPublishPreview(
            versionId,
            floorId,
            SpacePublishObjectTypes.Location,
            SpacePublishActions.Create,
            SpacePublishImpactCodes.WmsCreateLocation,
            true,
            25,
            "cursor",
            CancellationToken.None);

        Assert.Same(preview, result);
        service.VerifyAll();
    }
}
