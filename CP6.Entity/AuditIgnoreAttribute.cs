namespace CP6.Entity;

/// <summary>
/// 字段级审计排除特性（#4 字段审计 T1）。标注于 <see cref="IAuditable"/> 实体的属性上，
/// 令该字段不被字段级审计捕获（如密码等敏感列）。属于密钥三重防护之一
/// （另有内建拒名单 + 跳过主键/TenantId/元字段）。本身不映射任何列。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditIgnoreAttribute : Attribute
{
}
