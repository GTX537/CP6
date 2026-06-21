using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 安全事件审计服务实现。仿 OperLog 的 WriteLogSafely：审计写入失败仅记日志、绝不阻断登录/改密主流程。
/// 写入前对有界字符串字段防御式截断，避免超长输入在 SQL Server 触发截断异常→审计静默丢失。
/// </summary>
public class SecurityAuditService : ISecurityAuditService
{
    private readonly CP6Context _db;
    private readonly ILogger<SecurityAuditService>? _logger;

    public SecurityAuditService(CP6Context db, ILogger<SecurityAuditService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null)
    {
        try
        {
            _db.Sys_SecurityLogs.Add(new Sys_SecurityLog
            {
                UserId = userId,
                UserName = Trunc(userName, 100),
                RequestTenantCode = Trunc(requestTenantCode, 64),
                EventType = (int)type,
                ClientIp = Trunc(ip, 64),
                UserAgent = Trunc(ua, 256),
                Reason = Trunc(reason, 256),
                CreatedAt = DateTime.Now
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // 审计失败不阻断主流程，但记一行便于排查"审计为何缺失"
            _logger?.LogWarning(ex, "安全审计写入失败 type={EventType} user={UserName}", type, userName);
        }
    }

    /// <summary>截断到列长（含 null 透传），防超长输入触发 DB 截断异常。</summary>
    private static string? Trunc(string? value, int max)
        => value is { Length: var n } && n > max ? value[..max] : value;
}
