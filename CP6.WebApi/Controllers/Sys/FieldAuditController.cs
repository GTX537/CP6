using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 字段级审计查询（#4 字段审计 T5）。Sys_FieldAuditLog 为多租户实体，全局查询过滤自动按当前租户
/// 隔离（admin 仅见本租户审计）。列表端点按 entityName/entityKey/userId/时间段筛选 + 分页，仅返
/// changeCount 摘要（不返完整 changes 串，防 200 行×大文本负载，评审 R7）；record 端点按单记录
/// （entityName+entityKey）拉全部变更并按 ChangedAt 正序返完整 changes 原 JSON 串供前端回放。
/// </summary>
[ApiController]
[Route("api/sys/field-audit")]
[Authorize]
public class FieldAuditController : ControllerBase
{
    private readonly CP6Context _db;
    public FieldAuditController(CP6Context db) => _db = db;

    /// <summary>分页查询字段审计日志。资源键 sys-field-audit（MenuKey 由 RoutePath 派生），操作点 query。</summary>
    [HttpGet]
    [RequirePermission("sys-field-audit", "query")]
    public async Task<IActionResult> GetList(
        [FromQuery] string? entityName,
        [FromQuery] string? entityKey,
        [FromQuery] Guid? userId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);                 // 防 page<=0 → 负数 Skip 抛异常
        pageSize = Math.Clamp(pageSize, 1, 200);  // 防一次拉全表

        var q = _db.Sys_FieldAuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityName))
            q = q.Where(x => x.EntityName == entityName);
        if (!string.IsNullOrWhiteSpace(entityKey))
            q = q.Where(x => x.EntityKey == entityKey);
        if (userId.HasValue)
            q = q.Where(x => x.UserId == userId.Value);
        if (from.HasValue)
            q = q.Where(x => x.ChangedAt >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.ChangedAt < to.Value.AddDays(1));   // to 含当日（右开到次日零点）

        var total = await q.CountAsync();
        var raw = await q
            .OrderByDescending(x => x.ChangedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new { x.Id, x.EntityName, x.EntityKey, x.Operation, x.Changes, x.UserId, x.UserName, x.ChangedAt })
            .ToListAsync();

        // changeCount 摘要：不返完整 changes（防 200 行×大文本负载，评审 R7）。Count 在内存（JSON 反序列化）。
        var rows = raw.Select(x => new
        {
            x.Id,
            x.EntityName,
            x.EntityKey,
            x.Operation,
            changeCount = CountChanges(x.Changes),
            x.UserId,
            x.UserName,
            x.ChangedAt
        }).ToList();

        return Ok(new { rows, total });
    }

    /// <summary>单记录时间线：同 entityName+entityKey 全部变更按 ChangedAt 正序返完整 changes 原 JSON 串。</summary>
    [HttpGet("record")]
    [RequirePermission("sys-field-audit", "query")]
    public async Task<IActionResult> GetRecord([FromQuery] string entityName, [FromQuery] string entityKey)
    {
        var rows = await _db.Sys_FieldAuditLogs
            .Where(x => x.EntityName == entityName && x.EntityKey == entityKey)
            .OrderBy(x => x.ChangedAt)         // 时间线正序
            .Select(x => new { x.Id, x.Operation, x.Changes, x.UserId, x.UserName, x.ChangedAt })
            .ToListAsync();

        return Ok(new { rows });
    }

    private static int CountChanges(string json)
    {
        try { return System.Text.Json.JsonSerializer.Deserialize<List<FieldChangeDto>>(json)?.Count ?? 0; }
        catch { return 0; }
    }

    private sealed record FieldChangeDto(string field, string? old, string? @new);
}
