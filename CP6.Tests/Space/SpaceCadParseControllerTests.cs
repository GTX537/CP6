using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceCadParseControllerTests
{
    [Fact]
    public async Task Start_returns_accepted_location_and_replay_header()
    {
        var versionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var response = new StartSpaceCadParseResponse(
            jobId,
            "Queued",
            $"/api/space/design/v1/jobs/{jobId:D}",
            "/cad-parse",
            Source(sourceId, versionId),
            IdempotentReplay: true);
        var service = new Mock<ISpaceCadParseService>();
        service.Setup(item => item.StartAsync(
                versionId,
                sourceId,
                It.IsAny<StartSpaceCadParseRequest>(),
                "cad-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.StartParse(
            versionId,
            sourceId,
            "cad-key",
            Request(),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(response.JobStatusUrl, accepted.Location);
        Assert.Same(response, accepted.Value);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replay"]);
    }

    [Fact]
    public async Task Retry_returns_new_job_location_and_replay_header()
    {
        var versionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var originalJobId = Guid.NewGuid();
        var retryJobId = Guid.NewGuid();
        var response = new SpaceCadParseActionResponse(
            retryJobId,
            "Queued",
            $"/api/space/design/v1/jobs/{retryJobId:D}",
            "/cad-retry",
            IdempotentReplay: true);
        var service = new Mock<ISpaceCadParseService>();
        service.Setup(item => item.RetryAsync(
                versionId,
                sourceId,
                originalJobId,
                "retry-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.RetryParse(
            versionId,
            sourceId,
            originalJobId,
            "retry-key",
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(response.JobStatusUrl, accepted.Location);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replay"]);
    }

    [Fact]
    public async Task Upload_rejects_unknown_cad_format_before_service_call()
    {
        var service = new Mock<ISpaceCadParseService>();
        var controller = NewController(service.Object);
        var file = new FormFile(
            new MemoryStream([1, 2, 3]),
            0,
            3,
            "file",
            "warehouse.cad")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/octet-stream",
        };

        var error = await Assert.ThrowsAsync<SpaceProblemException>(() =>
            controller.UploadCadSource(
                Guid.NewGuid(),
                new UploadSpaceCadSourceForm
                {
                    SourceFormat = "Cad",
                    File = file,
                },
                CancellationToken.None));

        Assert.Equal(SpaceErrorCodes.CadParseInvalid, error.Code);
        service.VerifyNoOtherCalls();
    }

    private static SpaceCadParseController NewController(
        ISpaceCadParseService service) =>
        new(service, Mock.Of<ISpaceCadPreparationService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    private static StartSpaceCadParseRequest Request() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            SpaceCadUnit.Millimeter,
            1,
            "{}",
            new string('a', 64),
            Guid.NewGuid(),
            1,
            new string('b', 64),
            new string('c', 64));

    private static SpaceSourceDto Source(Guid sourceId, Guid versionId) =>
        new(
            sourceId,
            versionId,
            "Dxf",
            Guid.NewGuid(),
            "warehouse.dxf",
            new string('d', 64),
            "Ready",
            SpaceCadParseJobProcessor.Version,
            Guid.NewGuid(),
            1,
            "Millimeter",
            1,
            string.Empty);
}
