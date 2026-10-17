using System.Text.Json;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit.Abstractions;

namespace CP6.Tests.DatabaseCompatibility;

[Collection(Wp5ReportsRelationalCollection.Name)]
public sealed class DashboardRelationalTests(Wp5ReportsRelationalFixture fixture, ITestOutputHelper output)
{
    [Wp5ReportsFact]
    public async Task Summary_reads_empty_native_database()
    {
        output.WriteLine(fixture.SetupSummary);
        await fixture.RequireEmptyReportTablesAsync();
        await using var connection = fixture.CreateConnection();
        using var services = new ServiceCollection().AddDistributedMemoryCache().BuildServiceProvider();
        // This fact tests native empty-query/DTO behavior; real request binding is covered by DashboardTenantIsolationTests.
        var permissions = new Mock<ICurrentPermissionContext>();
        permissions.Setup(p => p.GetAsync()).ReturnsAsync(new UserPermissionContext { UserId = Guid.NewGuid() });
        var controller = new DashboardController(connection, new CacheService(services.GetRequiredService<IDistributedCache>()), new TenantContext(), permissions.Object);
        var response = Assert.IsType<OkObjectResult>(await controller.GetSummary());
        var body = JsonSerializer.SerializeToElement(response.Value);
        var summary = body.GetProperty("Summary").Deserialize<DashboardController.SummaryDto>();
        Assert.Equal(new DashboardController.SummaryDto(0, 0, 0, 0, 0, 0, 0, 0), summary);
        Assert.Empty(body.GetProperty("RecentOrders").EnumerateArray());
        Assert.Empty(body.GetProperty("WorkOrderStatus").EnumerateArray());
    }
}
