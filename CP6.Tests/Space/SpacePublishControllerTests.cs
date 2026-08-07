using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpacePublishControllerTests
{
    [Fact]
    public async Task Create_forwards_idempotency_key_and_returns_attempt_location()
    {
        var versionId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var request = new CreateSpacePublishAttemptRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            new string('a', 64),
            "approved-change");
        var response = new CreateSpacePublishAttemptResponse(
            Attempt(attemptId),
            IdempotentReplay: false);
        var service = new Mock<ISpacePublishOrchestrator>();
        service.Setup(value => value.StartAsync(
                versionId,
                request,
                "publish-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = new SpacePublishController(service.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.CreatePublishAttempt(
            versionId,
            request,
            "publish-key",
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(
            nameof(SpacePublishController.GetPublishAttempt),
            accepted.ActionName);
        Assert.Equal(attemptId, accepted.RouteValues!["attemptId"]);
        Assert.Equal("false", controller.Response.Headers["Idempotent-Replay"]);
        Assert.Same(response, accepted.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task Get_returns_the_persisted_attempt()
    {
        var attempt = Attempt(Guid.NewGuid());
        var service = new Mock<ISpacePublishOrchestrator>();
        service.Setup(value => value.GetAsync(
                attempt.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(attempt);
        var controller = new SpacePublishController(service.Object);

        var result = await controller.GetPublishAttempt(
            attempt.Id,
            CancellationToken.None);

        Assert.Same(attempt, result);
        service.VerifyAll();
    }

    private static SpacePublishAttemptDto Attempt(Guid id) =>
        new(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "cp6-wms-v1",
            new string('b', 64),
            "Completed",
            "Complete",
            DateTime.UtcNow,
            DateTime.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null,
            "Published.",
            Guid.NewGuid(),
            0,
            []);
}
