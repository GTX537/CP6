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
        var controller = new SpacePublishController(
            service.Object,
            Mock.Of<ISpaceHistoricalRepublishService>())
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
        var controller = new SpacePublishController(
            service.Object,
            Mock.Of<ISpaceHistoricalRepublishService>());

        var result = await controller.GetPublishAttempt(
            attempt.Id,
            CancellationToken.None);

        Assert.Same(attempt, result);
        service.VerifyAll();
    }

    [Fact]
    public async Task Retry_forwards_reason_and_idempotency_key()
    {
        var attemptId = Guid.NewGuid();
        var request = new RetrySpacePublishAttemptRequest(
            "Operator verified the WMS incident.",
            "Operation status is safe to query again.");
        var response = new RetrySpacePublishAttemptResponse(
            Attempt(attemptId),
            IdempotentReplay: false);
        var service = new Mock<ISpacePublishOrchestrator>();
        service.Setup(value => value.RetryAsync(
                attemptId,
                request,
                "retry-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = new SpacePublishController(
            service.Object,
            Mock.Of<ISpaceHistoricalRepublishService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.RetryPublishAttempt(
            attemptId,
            request,
            "retry-key",
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(attemptId, accepted.RouteValues!["attemptId"]);
        Assert.Equal("false", controller.Response.Headers["Idempotent-Replay"]);
        Assert.Same(response, accepted.Value);
        service.VerifyAll();
    }

    [Fact]
    public async Task Historical_republish_forwards_key_and_returns_operation_location()
    {
        var historicalVersionId = Guid.NewGuid();
        var request = new StartSpaceHistoricalRepublishRequest(
            Guid.NewGuid(),
            "Restore the last verified warehouse layout.",
            "CAB-42",
            "Restored layout");
        var republish = HistoricalRepublish(Guid.NewGuid(), historicalVersionId);
        var response = new StartSpaceHistoricalRepublishResponse(
            republish,
            IdempotentReplay: false);
        var history = new Mock<ISpaceHistoricalRepublishService>();
        history.Setup(value => value.StartAsync(
                historicalVersionId,
                request,
                "rollback-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = new SpacePublishController(
            Mock.Of<ISpacePublishOrchestrator>(),
            history.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = await controller.StartHistoricalRepublish(
            historicalVersionId,
            request,
            "rollback-key",
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(
            nameof(SpacePublishController.GetHistoricalRepublish),
            accepted.ActionName);
        Assert.Equal(republish.Id, accepted.RouteValues!["republishId"]);
        Assert.Equal("false", controller.Response.Headers["Idempotent-Replay"]);
        Assert.Same(response, accepted.Value);
        history.VerifyAll();
    }

    [Fact]
    public async Task Get_historical_republish_returns_persisted_operation()
    {
        var republish = HistoricalRepublish(Guid.NewGuid(), Guid.NewGuid());
        var history = new Mock<ISpaceHistoricalRepublishService>();
        history.Setup(value => value.GetAsync(
                republish.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(republish);
        var controller = new SpacePublishController(
            Mock.Of<ISpacePublishOrchestrator>(),
            history.Object);

        var result = await controller.GetHistoricalRepublish(
            republish.Id,
            CancellationToken.None);

        Assert.Same(republish, result);
        history.VerifyAll();
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
            Guid.NewGuid(),
            "Publish",
            "Succeeded",
            1,
            5,
            null,
            null,
            0,
            null,
            null,
            0,
            [],
            []);

    private static SpaceHistoricalRepublishDto HistoricalRepublish(
        Guid id,
        Guid historicalVersionId) =>
        new(
            id,
            Guid.NewGuid(),
            historicalVersionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "P0007",
            "Initializing",
            "Requested",
            "Restore the last verified warehouse layout.",
            "CAB-42",
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Queued",
            null,
            null,
            null);
}
