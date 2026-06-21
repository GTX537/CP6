using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 安全事件审计服务（S 类认证加固 T3）。把认证类事件写入 <see cref="Sys_SecurityLog"/>——
/// /api/auth 被全局 OperLogFilter 跳过，认证审计由此独立承担。审计失败不得阻断主流程。
/// </summary>
public interface ISecurityAuditService
{
    Task LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null);
}
