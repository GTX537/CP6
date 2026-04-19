using System.ComponentModel.DataAnnotations;

namespace CP6.Entity;

/// <summary>
/// 业务表公共基类 - 在 BaseEntity（Id/Creator/CreateDate/Modifier/ModifyDate）之上
/// 追加 业务场景常用的 逻辑删除 和 乐观锁（RowVersion）字段
/// 系统表（Sys_*）仍然直接继承 BaseEntity，不受影响
/// </summary>
public abstract class BaseBizEntity : BaseEntity
{
    /// <summary>
    /// 逻辑删除标记：false=有效, true=已删除
    /// 业务 Service 统一拦截，不做物理删除
    /// </summary>
    public bool IsDeleted { get; set; } = false;

    /// <summary>
    /// 乐观锁版本号（SQL Server ROWVERSION / timestamp）
    /// EF Core 会自动在 UPDATE 的 WHERE 里附加此列，保证并发安全
    /// 发生冲突时抛 DbUpdateConcurrencyException -> API 层返回 409
    /// </summary>
    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
