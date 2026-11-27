using Microsoft.AspNetCore.Mvc;

namespace CP6.Tests.Erp;

public sealed class EngineeringIntakeRoutesTests
{
    [Fact]
    public void Wp0560_registers_exact_Main_provider_routes()
    {
        var api = typeof(CP6.WebApi.Controllers.Plm.EngineeringRequirementController).Assembly;
        var routes = api.GetTypes().Where(type => typeof(ControllerBase).IsAssignableFrom(type))
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetCustomAttributes(typeof(HttpPostAttribute), false).Cast<HttpPostAttribute>())
            .Select(attribute => attribute.Template);

        Assert.Contains("/internal/plm/v1/engineering-handoff-next-context/query", routes);
        Assert.Contains("/internal/plm/v1/engineering-handoffs", routes);
        Assert.Contains("/internal/plm/v1/engineering-handoff-originals/query", routes);
    }
}
