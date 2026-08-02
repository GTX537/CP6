using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceExcelPreflightControllerTests
{
    [Fact]
    public async Task Start_returns_accepted_location_and_replay_header()
    {
        var versionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var response = new StartSpaceExcelPreflightResponse(
            jobId,
            "Queued",
            $"/api/space/design/v1/jobs/{jobId:D}",
            "/preview",
            "/report",
            Guid.NewGuid(),
            2,
            new string('a', 64),
            Source(sourceId, versionId),
            IdempotentReplay: true);
        var service = new Mock<ISpaceExcelPreflightService>();
        service.Setup(item => item.StartAsync(
                versionId,
                sourceId,
                It.IsAny<StartSpaceExcelPreflightRequest>(),
                "request-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.StartPreflight(
            versionId,
            sourceId,
            "request-key",
            new(response.MappingProfileId, response.MappingProfileVersion),
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(response.JobStatusUrl, accepted.Location);
        Assert.Same(response, accepted.Value);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replay"]);
    }

    [Fact]
    public async Task Upload_streams_excel_and_returns_scan_job_location()
    {
        var versionId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var response = new UploadSpaceExcelSourceResponse(
            new(
                fileId,
                "warehouse.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx",
                3,
                new string('b', 64),
                "Quarantined",
                null,
                string.Empty),
            Source(sourceId, versionId),
            jobId,
            $"/api/space/design/v1/jobs/{jobId:D}",
            Reused: false);
        var service = new Mock<ISpaceExcelPreflightService>();
        service.Setup(item => item.UploadAsync(
                versionId,
                "warehouse.xlsx",
                It.IsAny<string>(),
                It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);
        var formFile = new FormFile(
            new MemoryStream([1, 2, 3]),
            0,
            3,
            "file",
            "warehouse.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType =
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        };

        var result = await controller.UploadExcelSource(
            versionId,
            new UploadSpaceExcelSourceForm { File = formFile },
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(result);
        Assert.Equal(response.JobStatusUrl, accepted.Location);
        Assert.Same(response, accepted.Value);
    }

    [Fact]
    public async Task Report_is_private_attachment_with_nosniff()
    {
        var service = new Mock<ISpaceExcelPreflightService>();
        service.Setup(item => item.OpenErrorReportAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SpaceExcelPreflightReport(
                new MemoryStream([1, 2, 3]),
                "text/csv; charset=utf-8",
                "issues.csv"));
        var controller = NewController(service.Object);

        var result = await controller.DownloadErrorReport(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("text/csv; charset=utf-8", file.ContentType);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
        Assert.Contains(
            "issues.csv",
            controller.Response.Headers.ContentDisposition.ToString());
    }

    private static SpaceExcelPreflightController NewController(
        ISpaceExcelPreflightService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    private static SpaceSourceDto Source(Guid sourceId, Guid versionId) =>
        new(
            sourceId,
            versionId,
            "Excel",
            Guid.NewGuid(),
            "warehouse.xlsx",
            new string('c', 64),
            "Ready",
            null,
            null,
            null,
            null,
            null,
            string.Empty);
}
