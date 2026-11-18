using System.Data.Common;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
            var log = CreateRecord(type, userId, userName, requestTenantCode, ip, ua, reason);
            PrepareObservation(_db, log, true, createdNow: true);
            _db.Sys_SecurityLogs.Add(log);
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // 审计失败不阻断主流程，但记一行便于排查"审计为何缺失"
            _logger?.LogWarning("安全审计写入失败 {EventType} {FailureType}", type, ex.GetType().Name);
        }
    }

    internal static async Task SaveWithinRootAsync(CP6Context db, Sys_SecurityLog audit, bool bestEffort, CancellationToken ct)
    {
        PrepareObservation(db, audit, bestEffort, createdNow: false);
        if (!bestEffort)
        {
            db.Sys_SecurityLogs.Add(audit);
            await db.SaveChangesAsync(ct);
            return;
        }

        var transaction = db.Database.CurrentTransaction!;
        const string savepoint = "security_audit_v1";
        await transaction.CreateSavepointAsync(savepoint, ct);
        try
        {
            db.Sys_SecurityLogs.Add(audit);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception error) when (error is DbUpdateException or DbException)
        {
            await transaction.RollbackToSavepointAsync(savepoint, ct);
            db.Entry(audit).State = EntityState.Detached;
            db.GetService<ILoggerFactory>().CreateLogger<SecurityAuditService>()
                .LogWarning("Security audit {EventType} write failed for tenant {TenantId}, user {UserId}", audit.EventType, audit.TenantId, audit.UserId);
        }
        await transaction.ReleaseSavepointAsync(savepoint, ct);
    }

    private static void PrepareObservation(CP6Context db, Sys_SecurityLog audit, bool bestEffort, bool createdNow)
    {
        if (!db.Database.IsNpgsql() || audit.AuditEvidenceJson is not null) return;
        var now = DateTimeOffset.UtcNow;
        var platform = (audit.TenantId == Guid.Empty ? db.CurrentTenantId : audit.TenantId) == TenantContext.DefaultTenant;
        audit.AuditAtUtc = createdNow ? now : null;
        // 登录请求中的名称、UA 和自由文本不证明身份，也不作为新日志诊断内容。
        if (audit.UserId is null) audit.UserName = null;
        audit.UserAgent = null;
        if (!platform || !System.Net.IPAddress.TryParse(audit.ClientIp, out _)) audit.ClientIp = null;
        if (audit.Reason is { } reason && !System.Text.RegularExpressions.Regex.IsMatch(reason, @"\AE-SEC-[0-9]{3}\z")) audit.Reason = null;
        audit.AuditEvidenceJson = new AuditEvidenceV1(createdNow ? "SECURITY_EVENT_UTC" : "LEGACY_OCCURRENCE",
            bestEffort ? "BEST_EFFORT_SECURITY" : "REQUIRED_AUDIT", "UNBOUND", Outcome: "OWNER_RECORDED", RecordedAtUtc: now,
            DataPolicy: platform ? "PLATFORM_SECURITY_V1" : "TENANT_SECURITY_V1", Action: ((SecurityEventType)audit.EventType).ToString()).Encode();
    }

    internal static Sys_SecurityLog CreateRecord(SecurityEventType type, Guid? userId, string? userName,
        string? requestTenantCode, string? ip, string? ua, string? reason)
        => new()
        {
            UserId = userId,
            UserName = Trunc(userName, 100),
            RequestTenantCode = Trunc(requestTenantCode, 64),
            EventType = (int)type,
            ClientIp = Trunc(ip, 64),
            UserAgent = Trunc(ua, 256),
            Reason = Trunc(reason, 256),
            CreatedAt = DateTime.Now
        };

    /// <summary>截断到列长（含 null 透传），防超长输入触发 DB 截断异常。</summary>
    private static string? Trunc(string? value, int max)
        => value is { Length: var n } && n > max ? value[..max] : value;
}
