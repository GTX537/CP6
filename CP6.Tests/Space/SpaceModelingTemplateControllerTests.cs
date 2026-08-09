using CP6.Space.Application;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceModelingTemplateControllerTests
{
    [Fact]
    public void Download_returns_versioned_excel_with_defensive_headers()
    {
        var template = new SpaceModelingTemplateFile(
            [(byte)'P', (byte)'K', 3, 4],
            "cp6-space-standard-model-v1.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "1.0");
        var modelingTemplates = new Mock<ISpaceModelingTemplateService>();
        modelingTemplates
            .Setup(service => service.CreateStandardExcelTemplate())
            .Returns(template);
        var controller = new SpaceDesignV1Controller(
            Mock.Of<ISpaceDesignV1Service>(),
            Mock.Of<ISpaceUnderlayV1Service>(),
            Mock.Of<ISpaceWmsAdoptionService>(),
            modelingTemplates.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = Assert.IsType<FileContentResult>(
            controller.DownloadStandardExcelTemplate());

        Assert.Equal(template.Content, result.FileContents);
        Assert.Equal(template.ContentType, result.ContentType);
        Assert.Equal(template.FileName, result.FileDownloadName);
        Assert.Equal(
            "private, no-store",
            controller.Response.Headers.CacheControl.ToString());
        Assert.Equal(
            "nosniff",
            controller.Response.Headers.XContentTypeOptions.ToString());
        Assert.Equal(
            template.SchemaVersion,
            controller.Response.Headers["X-Space-Template-Schema"].ToString());
        Assert.Contains(
            "attachment",
            controller.Response.Headers.ContentDisposition.ToString());
    }
}
