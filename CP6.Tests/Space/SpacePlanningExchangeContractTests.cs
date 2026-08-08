using System.Reflection;
using CP6.Core.Auth;
using CP6.Space.Application;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Space;

public sealed class SpacePlanningExchangeContractTests
{
    [Fact]
    public void Endpoint_uses_branch_scoped_read_permission_and_binary_media_type()
    {
        var controller = typeof(SpacePlanningExchangeController);
        var route = Assert.Single(
            controller.GetCustomAttributes<RouteAttribute>());
        Assert.Equal(
            "api/space/planning/v1/sites/{siteId:guid}/scenario-branches/" +
            "{branchId:guid}/exports",
            route.Template);
        var method = controller.GetMethod(
            nameof(SpacePlanningExchangeController.DownloadGlb))!;
        Assert.Equal(
            "gltf",
            Assert.Single(method.GetCustomAttributes<HttpGetAttribute>())
                .Template);
        var permission = Assert.Single(
            CustomAttributeData.GetCustomAttributes(method),
            value => value.AttributeType == typeof(RequirePermissionAttribute));
        Assert.Equal("space", permission.ConstructorArguments[0].Value);
        Assert.Equal(
            "planning:exchange:read",
            permission.ConstructorArguments[1].Value);
        var response = Assert.Single(
            method.GetCustomAttributes<ProducesResponseTypeAttribute>());
        Assert.Equal(StatusCodes.Status200OK, response.StatusCode);
        Assert.Equal(typeof(FileContentResult), response.Type);
    }

    [Fact]
    public async Task Download_sets_integrity_and_no_store_headers()
    {
        var content = new byte[] { 1, 2, 3 };
        var hash = new string('a', 64);
        var controller = new SpacePlanningExchangeController(
            new StubService(new SpacePlanningExchangeFile(
                content,
                "scenario.glb",
                "model/gltf-binary",
                "cp6.space.planning.gltf.v1",
                hash)))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

        var result = Assert.IsType<FileContentResult>(
            await controller.DownloadGlb(Guid.NewGuid(), Guid.NewGuid()));

        Assert.Equal(content, result.FileContents);
        Assert.Equal("model/gltf-binary", result.ContentType);
        Assert.Equal("scenario.glb", result.FileDownloadName);
        Assert.Equal("private, no-store", controller.Response.Headers.CacheControl);
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions);
        Assert.Equal($"\"{hash}\"", controller.Response.Headers.ETag);
        Assert.Equal(
            "cp6.space.planning.gltf.v1",
            controller.Response.Headers["X-Space-Exchange-Schema"]);
        Assert.Equal(
            hash,
            controller.Response.Headers["X-Space-Exchange-Sha256"]);
    }

    private sealed class StubService(SpacePlanningExchangeFile value)
        : ISpacePlanningExchangeService
    {
        public Task<SpacePlanningExchangeFile> ExportGlbAsync(
            Guid siteId,
            Guid branchId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(value);
    }
}
