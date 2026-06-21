using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 密码策略服务（S 类认证加固 T2）：复杂度校验 + 历史不可重用 + 有效期判定。
/// 失败抛 <see cref="InvalidOperationException"/>（错误码 E-SEC-0xx，Core 层惯例）——
/// 由 WebApi 控制器在边界转 BizException 经中间件本地化。
/// </summary>
public interface IPasswordPolicyService
{
    /// <summary>校验明文是否满足复杂度策略；不满足抛 E-SEC-004。</summary>
    void Validate(string plain);

    /// <summary>校验新明文不得命中该用户最近 N 条历史密码；命中抛 E-SEC-005。</summary>
    Task CheckHistoryAsync(Guid userId, string plain);

    /// <summary>
    /// 把旧密码哈希记入历史并裁剪到最近 HistoryCount 条。
    /// 仅在共享 DbContext 上 Add/Remove，<b>不 SaveChanges</b>——由调用方与改密的其它写入合并为一次保存（保持原子）。
    /// HistoryCount&lt;=0（历史关闭）时不记录。
    /// </summary>
    Task RecordHistoryAsync(Guid userId, string oldHash);

    /// <summary>当前密码是否已过有效期（ExpiryDays>0 且超期）。</summary>
    bool IsExpired(Sys_User user);
}
