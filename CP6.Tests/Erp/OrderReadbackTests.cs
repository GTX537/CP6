using CP6.Core.Services.Erp;
using CP6.Entity.DTOs;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text.Json;

namespace CP6.Tests.Erp;

public sealed class OrderReadbackTests
{
    [Theory]
    [InlineData(403, false)]
    [InlineData(503, true)]
    public async Task Committed_write_is_not_reported_as_failed_when_independent_readback_is_unavailable(int status, bool update)
    {
        var service = new Mock<IOrderService>();
        var dto = new OrderDto();
        service.Setup(s => s.CreateAsync(dto, null)).ReturnsAsync("SAVED");
        service.Setup(s => s.UpdateAsync("SAVED", dto, null)).Returns(Task.CompletedTask);
        service.Setup(s => s.GetByWebOrderNoAsync("SAVED", false))
            .ThrowsAsync(new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", status));
        var controller = new OrderController(service.Object);
        var response = Assert.IsType<OkObjectResult>(update ? await controller.Update("SAVED", dto) : await controller.Create(dto));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        Assert.Equal(0, json.RootElement.GetProperty("code").GetInt32());
        Assert.True(json.RootElement.GetProperty("readbackUnavailable").GetBoolean());
        Assert.Equal("SAVED", json.RootElement.GetProperty("webOrderNo").GetString());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        service.Verify(s => s.GetByWebOrderNoAsync("SAVED", false), Times.Once);
        if (update) service.Verify(s => s.UpdateAsync("SAVED", dto, null), Times.Once);
        else service.Verify(s => s.CreateAsync(dto, null), Times.Once);
    }
}
