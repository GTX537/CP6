using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceExcelMappingControllerTests
{
    [Fact]
    public async Task Save_created_profile_returns_location_and_replay_header()
    {
        var profileId = Guid.NewGuid();
        var definition = Definition();
        var response = new SaveSpaceExcelMappingProfileResponse(
            new(
                profileId,
                "Vendor A",
                "Tenant",
                1,
                false,
                new string('a', 64),
                definition,
                null,
                null,
                "AQID",
                DateTime.UtcNow,
                Guid.NewGuid()),
            Created: true,
            IdempotentReplay: false);
        var service = new Mock<ISpaceExcelMappingService>();
        service.Setup(item => item.SaveProfileAsync(
                It.IsAny<SaveSpaceExcelMappingProfileRequest>(),
                "request-key",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.SaveProfile(
            "request-key",
            new(null, "Vendor A", definition),
            CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(SpaceExcelMappingController.GetProfile), created.ActionName);
        Assert.Equal(profileId, created.RouteValues!["profileId"]);
        Assert.Same(response, created.Value);
        Assert.Equal("false", controller.Response.Headers["Idempotent-Replay"]);
    }

    [Fact]
    public async Task Save_replay_of_existing_profile_returns_ok_and_replay_header()
    {
        var definition = Definition();
        var response = new SaveSpaceExcelMappingProfileResponse(
            new(
                Guid.NewGuid(),
                "Vendor A",
                "Tenant",
                2,
                false,
                new string('b', 64),
                definition,
                null,
                null,
                "BAUG",
                DateTime.UtcNow,
                Guid.NewGuid()),
            Created: false,
            IdempotentReplay: true);
        var service = new Mock<ISpaceExcelMappingService>();
        service.Setup(item => item.SaveProfileAsync(
                It.IsAny<SaveSpaceExcelMappingProfileRequest>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = NewController(service.Object);

        var result = await controller.SaveProfile(
            "request-key",
            new(response.Profile.Id, "Vendor A", definition, "AQID"),
            CancellationToken.None);

        Assert.Same(response, Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("true", controller.Response.Headers["Idempotent-Replay"]);
    }

    private static SpaceExcelMappingController NewController(
        ISpaceExcelMappingService service) =>
        new(service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            },
        };

    private static SpaceExcelMappingDefinitionDto Definition() =>
        new(1, "Warning", "Reject", "Reject", []);
}
