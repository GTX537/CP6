using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;
using CP6.WebApi.Controllers.Space;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace CP6.Tests.Space;

public sealed class SpaceValidationControllerTests
{
    [Fact]
    public async Task Create_returns_accepted_link_to_persisted_run()
    {
        var versionId = Guid.NewGuid();
        var dto = Validation(versionId);
        var response = new CreateSpaceValidationResponse(dto, Reused: false);
        var service = new Mock<ISpaceValidationService>();
        service.Setup(value => value.RequestValidationAsync(
                versionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);
        var controller = new SpaceValidationController(service.Object);

        var result = await controller.CreateValidation(
            versionId,
            CancellationToken.None);

        var accepted = Assert.IsType<AcceptedAtActionResult>(result);
        Assert.Equal(
            nameof(SpaceValidationController.GetValidation),
            accepted.ActionName);
        Assert.Equal(dto.Id, accepted.RouteValues!["validationId"]);
        Assert.Same(response, accepted.Value);
    }

    [Fact]
    public async Task Get_returns_authoritative_service_result()
    {
        var dto = Validation(Guid.NewGuid());
        var service = new Mock<ISpaceValidationService>();
        service.Setup(value => value.GetValidationAsync(
                dto.Id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);
        var controller = new SpaceValidationController(service.Object);

        var result = await controller.GetValidation(
            dto.Id,
            CancellationToken.None);

        Assert.Same(dto, result);
    }

    [Fact]
    public async Task Default_profile_freezes_authoritative_wms_capability_hash()
    {
        var tenantId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        var snapshot = SpaceWmsCapabilitySnapshot.Create(
            "verified-wms",
            SpaceWmsDataSourceKind.Real,
            SpaceWmsCertificationLevel.CertifiedIdempotent,
            new SpaceWmsCapabilities(
                false,
                true,
                true,
                false,
                true,
                true,
                true,
                true,
                true,
                true,
                500,
                "^[A-Z0-9-]+$",
                30),
            DateTimeOffset.UtcNow);
        var adapter = new Mock<ISpaceWmsAdapter>();
        adapter.Setup(value => value.GetCapabilitiesAsync(
                It.Is<SpaceWmsContext>(context =>
                    context.TenantId == tenantId &&
                    context.SiteId == siteId &&
                    context.CorrelationId == correlationId),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
        using var services = new ServiceCollection()
            .AddSingleton(adapter.Object)
            .BuildServiceProvider();
        var provider =
            new DefaultSpaceValidationProfileProvider(services);

        var profile = await provider.GetProfileAsync(
            tenantId,
            siteId,
            correlationId);

        Assert.Equal(snapshot.AdapterId, profile.AdapterId);
        Assert.Equal(snapshot.CapabilityHash, profile.CapabilityHash);
        Assert.Equal(
            snapshot.Capabilities.CodeMaxLength,
            profile.MaxLocationCodeLength);
        Assert.Equal(
            snapshot.Capabilities.AllowedCodePattern,
            profile.LocationCodePattern);
    }

    private static SpaceValidationRunDto Validation(Guid versionId) =>
        new(
            Guid.NewGuid(),
            versionId,
            3,
            new string('a', 64),
            SpaceValidationRuleSet.Version,
            "cp6-wms-v1",
            new string('b', 64),
            "Queued",
            0,
            0,
            0,
            DateTime.UtcNow,
            Guid.NewGuid(),
            null,
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            string.Empty,
            []);
}
