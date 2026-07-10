using CP6.Core.Auth;
using CP6.Core.Services.Wms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

/// <summary>MSBBWM330 — IoT センサ Web API</summary>
[ApiController]
[Route("api/wms/iot")]
[Authorize]
public class IotMonitorController : ControllerBase
{
    private readonly IIotService _svc;
    public IotMonitorController(IIotService svc) => _svc = svc;
    private string? CurrentUser => User?.Identity?.Name;

    // ─── センサマスタ ───
    [HttpGet("sensors")]
    public async Task<IActionResult> SearchSensors([FromQuery] string? warehouseCd, [FromQuery] string? sensorType)
        => Ok(new { code = 0, message = "OK", data = await _svc.SearchSensorsAsync(warehouseCd, sensorType) });

    [HttpGet("sensors/{id}")]
    public async Task<IActionResult> GetSensor(string id)
    {
        var s = await _svc.GetSensorAsync(id);
        if (s == null) return NotFound(new { code = 404, message = "WM-MSG-070" });
        return Ok(new { code = 0, message = "OK", data = s });
    }

    [HttpPost("sensors")]
    [RequirePermission("wms-iot", "add")]
    public async Task<IActionResult> CreateSensor([FromBody] IotSensorDto dto)
    {
        try
        {
            var id = await _svc.CreateSensorAsync(dto, CurrentUser);
            return Ok(new { code = 0, message = "WM-MSG-071", data = new { sensorId = id } });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpPut("sensors/{id}")]
    [RequirePermission("wms-iot", "edit")]
    public async Task<IActionResult> UpdateSensor(string id, [FromBody] IotSensorDto dto)
    {
        try { await _svc.UpdateSensorAsync(id, dto, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    [HttpDelete("sensors/{id}")]
    [RequirePermission("wms-iot", "del")]
    public async Task<IActionResult> DeleteSensor(string id)
    {
        try { await _svc.DeleteSensorAsync(id, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    // ─── 計測値 ───
    [HttpGet("sensors/{id}/readings")]
    public async Task<IActionResult> GetReadings(string id, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int limit = 200)
        => Ok(new { code = 0, message = "OK", data = await _svc.GetReadingsAsync(id, from, to, limit) });

    [HttpPost("sensors/{id}/readings")]
    [RequirePermission("wms-iot", "ingest")]
    public async Task<IActionResult> PostReading(string id, [FromBody] PostReadingReq req)
    {
        try { await _svc.PostReadingAsync(id, req.Value, req.ReadAt, CurrentUser); return Ok(new { code = 0, message = "WM-MSG-071" }); }
        catch (InvalidOperationException ex) { return BadRequest(new { code = 400, message = ex.Message }); }
    }

    /// <summary>疑似データ生成（デモ用）— 全センサ × N 件</summary>
    [HttpPost("simulate")]
    [RequirePermission("wms-iot", "simulate")]
    public async Task<IActionResult> Simulate([FromQuery] int count = 1)
    {
        var n = await _svc.SimulateAsync(count, CurrentUser);
        return Ok(new { code = 0, message = "OK", data = new { generated = n } });
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts()
        => Ok(new { code = 0, message = "OK", data = await _svc.CurrentAlertsAsync() });

    public class PostReadingReq
    {
        public decimal Value { get; set; }
        public DateTime? ReadAt { get; set; }
    }
}
