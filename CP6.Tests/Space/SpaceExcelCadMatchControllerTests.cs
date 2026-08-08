using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceExcelCadMatchControllerTests
{
    [Fact]
    public async Task Start_returns_authoritative_job_location_and_replay_header()
    {
        var versionId = Guid.NewGuid();
        var request = Request();
        var response = new StartSpaceExcelCadMatchResponse(
            Guid.NewGuid(),
            "Queued",
            $"/api/space/design/v1/versions/{versionId:D}/excel-cad-matches/" +
            $"{Guid.NewGuid():D}",
            IdempotentReplay: true);
        var service = new Mock<ISpaceExcelCadMatchService>();
        service.Setup(item => item.StartAsync(
                versionId,
                request,
                "match-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.StartMatch(
            versionId,
            "match-key",
            request,
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(response.JobStatusUrl, accepted.Location);
        Assert.Same(response, accepted.Value);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replay"]);
    }

    [Fact]
    public async Task Get_forwards_server_side_filters_and_paging()
    {
        var versionId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var response = new SpaceExcelCadMatchDto(
            jobId,
            versionId,
            "Succeeded",
            SpaceExcelCadMatchJobProcessor.Version,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            7,
            Guid.NewGuid(),
            new string('a', 64),
            new string('b', 64),
            false,
            null,
            0,
            0,
            null,
            [],
            null,
            null);
        var service = new Mock<ISpaceExcelCadMatchService>();
        service.Setup(item => item.GetAsync(
                versionId,
                jobId,
                "Conflict",
                "R-01",
                "H:160",
                true,
                25,
                "cursor-1",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.GetMatch(
            versionId,
            jobId,
            "Conflict",
            "R-01",
            "H:160",
            true,
            25,
            "cursor-1",
            CancellationToken.None);

        Assert.Same(response, result);
        service.VerifyAll();
    }

    [Fact]
    public async Task Confirm_returns_apply_job_location_and_replay_header()
    {
        var versionId = Guid.NewGuid();
        var matchJobId = Guid.NewGuid();
        var request = new ConfirmSpaceExcelCadMatchRequest(
            true,
            Guid.NewGuid(),
            new string('a', 64),
            7);
        var response = new ConfirmSpaceExcelCadMatchResponse(
            matchJobId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Queued",
            "/confirmation-status",
            IdempotentReplay: true);
        var apply = new Mock<ISpaceExcelCadApplyService>();
        apply.Setup(item => item.ConfirmAsync(
                versionId,
                matchJobId,
                request,
                "confirm-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(
            new Mock<ISpaceExcelCadMatchService>().Object,
            apply.Object);

        var result = await controller.ConfirmMatch(
            versionId,
            matchJobId,
            "confirm-key",
            request,
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(response.JobStatusUrl, accepted.Location);
        Assert.Same(response, accepted.Value);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replay"]);
        apply.VerifyAll();
    }

    [Fact]
    public async Task Get_confirmation_returns_typed_apply_status()
    {
        var versionId = Guid.NewGuid();
        var matchJobId = Guid.NewGuid();
        var applyJobId = Guid.NewGuid();
        var response = new SpaceExcelCadApplyDto(
            matchJobId,
            applyJobId,
            Guid.NewGuid(),
            "Running",
            7,
            null,
            false,
            null,
            null);
        var apply = new Mock<ISpaceExcelCadApplyService>();
        apply.Setup(item => item.GetAsync(
                versionId,
                matchJobId,
                applyJobId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(
            new Mock<ISpaceExcelCadMatchService>().Object,
            apply.Object);

        var result = await controller.GetConfirmation(
            versionId,
            matchJobId,
            applyJobId,
            CancellationToken.None);

        Assert.Same(response, result);
        apply.VerifyAll();
    }

    private static SpaceExcelCadMatchController NewController(
        ISpaceExcelCadMatchService service,
        ISpaceExcelCadApplyService? applyService = null) => new(
            service,
            applyService ?? new Mock<ISpaceExcelCadApplyService>().Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    private static StartSpaceExcelCadMatchRequest Request() => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        7);
}
