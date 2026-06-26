namespace CP6.Entity;

/// <summary>
/// 标记承载个人身份信息（PII）的属性。S 类 #5 租户合规（GDPR 被遗忘权）：
/// 数据主体擦除据此匿名化/置空、导出据此剔除密钥（T7 消费；T1 此刻仅为惰性标记）。
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PiiFieldAttribute : Attribute
{
    /// <summary>擦除模式：占位符替换（默认）或直接置 null。</summary>
    public PiiErase Mode { get; init; } = PiiErase.Placeholder;
}

/// <summary>PII 擦除模式。</summary>
public enum PiiErase { Placeholder, Null }
