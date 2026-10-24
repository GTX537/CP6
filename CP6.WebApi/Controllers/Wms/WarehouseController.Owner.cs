using System.Text.Json;
using CP6.Core.Services.Erp;
using CP6.Entity.DTOs.Wms;
using Microsoft.AspNetCore.Mvc;

namespace CP6.WebApi.Controllers.Wms;

public partial class WarehouseController
{
    [HttpGet("owner-v1/warehouses")]
    public async Task<IActionResult> WarehousesOwner([FromQuery] WarehouseQueryV1 query,CancellationToken ct)
        =>Ok(new {code=0,data=await Owner.WarehousesAsync(query,ct)});
    [HttpGet("owner-v1/warehouses/{id:guid}")]
    public async Task<IActionResult> WarehouseOwner(Guid id,CancellationToken ct)
    {var row=await Owner.WarehouseAsync(id,ct);Response.Headers.ETag=row.Etag;return Ok(new {code=0,data=row});}
    [HttpGet("owner-v1/warehouses/{id:guid}/locations")]
    public async Task<IActionResult> LocationsOwner(Guid id,[FromQuery] WarehouseQueryV1 query,CancellationToken ct)
        =>Ok(new {code=0,data=await Owner.LocationsAsync(id,query,ct)});
    [HttpGet("owner-v1/locations/{id:guid}")]
    public async Task<IActionResult> LocationOwner(Guid id,CancellationToken ct)
    {var row=await Owner.LocationAsync(id,ct);Response.Headers.ETag=row.Etag;return Ok(new {code=0,data=row});}
    // The native Owner chooses query for immutable replay and edit for new effects under one fence.
    [HttpPatch("owner-v1/{kind}/{id:guid}/name")]
    public async Task<IActionResult> RenameOwner(string kind,Guid id,[FromBody] JsonElement patch,CancellationToken ct)
    {
        if(!Guid.TryParseExact(Request.Headers["Idempotency-Key"].ToString(),"D",out var operation)||operation==Guid.Empty)
            throw new OrderPriceCorrectionException("ORIGINAL_OPERATION_REQUIRED",400);
        var result=await Owner.RenameAsync(kind,id,operation,Request.Headers.IfMatch.ToString(),patch,ct);
        Response.Headers.ETag=result.Etag;return Ok(new {code=0,data=result});
    }
    [HttpGet("owner-v1/{kind}/operation-scope")]
    public async Task<IActionResult> MasterOperationScope(string kind,CancellationToken ct)
        =>Ok(new {code=0,data=await Owner.ScopeAsync(kind,ct)});
    [HttpGet("owner-v1/{kind}/{id:guid}/operations/{operation:guid}")]
    public async Task<IActionResult> MasterOperation(string kind,Guid id,Guid operation,CancellationToken ct)
        =>Ok(new {code=0,data=await Owner.OperationAsync(kind,id,operation,ct:ct)});
    [HttpPost("owner-v1/{kind}/{id:guid}/operations/{operation:guid}/close")]
    public async Task<IActionResult> CloseMasterOperation(string kind,Guid id,Guid operation,CancellationToken ct)
        =>Ok(new {code=0,data=await Owner.OperationAsync(kind,id,operation,true,ct)});
}
