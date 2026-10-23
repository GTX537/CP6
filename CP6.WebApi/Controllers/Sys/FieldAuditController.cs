using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Sys;
using CP6.WebApi.Controllers.Erp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Controllers.Sys;

/// <summary>
/// 字段级审计查询（#4 字段审计 T5）。Sys_FieldAuditLog 为多租户实体，全局查询过滤自动按当前租户
/// 隔离（admin 仅见本租户审计）。列表端点按 entityName/entityKey/userId/时间段筛选 + 分页，仅返
/// changeCount 摘要（不返完整 changes 串，防 200 行×大文本负载，评审 R7）；record 端点按单记录
/// （entityName+entityKey）经当前业务Owner读取有界安全投影，不返回原 changes JSON。
/// </summary>
[ApiController]
[Route("api/sys/field-audit")]
[Authorize]
[OrderOwnerResult]
public class FieldAuditController : ControllerBase
{
    private readonly CP6Context _db;
    private readonly FieldAuditReadOwnerV1? _owner;
    public FieldAuditController(CP6Context db, FieldAuditReadOwnerV1? owner = null) { _db = db; _owner = owner; }

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
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new { x.Id, x.EntityName, x.EntityKey, x.Operation, x.Changes, x.UserId, x.UserName, x.ChangedAt })
            .ToListAsync();

        // changeCount 摘要：不返完整 changes（防 200 行×大文本负载，评审 R7）。Count 在内存（JSON 反序列化）。
        var rows = raw.Select(x =>
        {
            var summary = FieldAuditChangesV1.Summarize(x.Changes);
            return new {
            x.Id,
            x.EntityName,
            x.EntityKey,
            x.Operation,
            changeCount = summary.ChangeCount,
            evidenceStatus = summary.EvidenceStatus,
            x.UserId,
            x.UserName,
            x.ChangedAt
            };
        }).ToList();

        return Ok(new { rows, total });
    }

    /// <summary>当前Owner安全时间线；游标仅定位原行，不携带权限。</summary>
    [HttpGet("record")]
    [RequirePermission("sys-field-audit", "query")]
    public async Task<IActionResult> GetRecord([FromQuery] string entityName, [FromQuery] string entityKey,
        [FromQuery] Guid? afterId = null, [FromQuery] int pageSize = 100, CancellationToken ct = default)
    {
        if (_owner?.Owns(_db) != true) throw new OrderPriceCorrectionException("DETAIL_UNAVAILABLE", 503);
        return Ok(await _owner.ReadAsync(entityName, entityKey, afterId, pageSize, ct));
    }
}
