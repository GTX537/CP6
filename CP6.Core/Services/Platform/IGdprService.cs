namespace CP6.Core.Services.Platform;

/// <summary>
/// GDPR 双粒度数据导出 / 被遗忘权擦除（多租户合规 #5 块③）。导出整租户/单主体（均剔密钥）；
/// 擦除单主体（匿名化）/整租户（anonymize 匿名化 | purge 物理删除，purge 显式 opt-in）。
/// 导出返回 JSON <see cref="Stream"/>（UTF-8，position 0）。
/// </summary>
public interface IGdprService
{
    /// <summary>导出整租户数据包（Sys_Tenant 行 + 所有 owner 表行，逐行剔密钥）。租户不存在 → E-SEC-032。</summary>
    Task<Stream> ExportTenantAsync(Guid tenantId);

    /// <summary>导出单数据主体（Sys_User + 其安全日志/操作日志，剔密钥）。用户不存在 → E-SEC-032。</summary>
    Task<Stream> ExportSubjectAsync(Guid userId);

    /// <summary>擦除单数据主体（匿名化 + 重置密码哈希 + 停用 + 吊销 refresh 令牌族，保留行 + Id）。</summary>
    Task EraseSubjectAsync(Guid userId);

    /// <summary>擦除整租户：<c>anonymize</c>=PII 匿名化 + 停租户；<c>purge</c>=按拓扑物理删除（仅 relational）。</summary>
    Task EraseTenantAsync(Guid tenantId, string mode);
}
