using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpacePublishActivityControllerTests
{
    [Fact]
    public async Task Get_forwards_site_filter_and_cursor()
    {
        var siteId = Guid.NewGuid();
        var expected = new SpacePage<SpacePublishAttemptSummaryDto>([], "next");
        var service = new Mock<ISpacePublishActivityService>();
        service.Setup(value => value.GetBySiteAsync(
                siteId,
                "WaitingRetry",
                10,
                "cursor",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var controller = new SpacePublishActivityController(service.Object);

        var actual = await controller.GetPublishAttempts(
            siteId,
            "WaitingRetry",
            10,
            "cursor",
            CancellationToken.None);

        Assert.Same(expected, actual);
        service.VerifyAll();
    }
}
