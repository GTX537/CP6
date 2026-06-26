using System.Reflection;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Platform;

/// <summary>
/// GDPR（多租户合规 #5 块③ T7）字段策略：密钥拒名单 + PII 反射擦除。
/// <para><see cref="IsSensitive"/>：与 #4 字段审计 <c>CP6Context.IsSecretField</c> 同源的拒名单——
/// 导出剔密钥（export 时跳过命中字段）+ 擦除跳过（不把哈希写空破坏 FK/约束）。</para>
/// <para><see cref="EraseSubject"/>：反射 <c>Sys_User</c> 的 <see cref="PiiFieldAttribute"/> 列按 Mode 擦，
/// 并显式匿名化 UserName/重置 Password（新随机哈希）/停用，<b>保留行 + Id</b>（FK 完整性）。</para>
/// </summary>
public static class SensitiveFieldPolicy
{
    /// <summary>
    /// 密钥字段判定（大小写不敏感，后缀/全名匹配）。与 #4 <c>CP6Context.IsSecretField</c> 完全镜像：
    /// <c>Password</c> / 以 <c>secret</c>·<c>hash</c> 结尾 / <c>tokenhash</c> / <c>salt</c> /
    /// <c>clientsecretprotected</c> / <c>twofactorsecret</c>。
    /// </summary>
    public static bool IsSensitive(string columnName)
    {
        if (string.IsNullOrEmpty(columnName)) return false;
        var n = columnName.ToLowerInvariant();
        return n == "password" || n.EndsWith("secret") || n.EndsWith("hash")
            || n == "tokenhash" || n == "salt" || n == "clientsecretprotected" || n == "twofactorsecret";
    }

    /// <summary>
    /// 数据主体（用户）匿名化：①反射所有 <see cref="PiiFieldAttribute"/> 标注列按 Mode 擦
    /// （Placeholder=<c>REDACTED-{Id前8}</c>，Null=置 null）；②显式 <c>UserName=anon-{Id前8}</c>、
    /// <c>Password=新随机哈希</c>、<c>Enable=false</c>。<b>不删行、不改 Id</b>（保 FK 引用完整）。
    /// hasher 由调用方注入（避免本静态方法依赖 DI/反射构造哈希器）。
    /// </summary>
    public static void EraseSubject(CP6Context db, Sys_User user, IPasswordHasher hasher)
    {
        var idShort = user.Id.ToString("N")[..8];

        foreach (var p in typeof(Sys_User).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var pii = p.GetCustomAttribute<PiiFieldAttribute>();
            if (pii == null || !p.CanWrite) continue;
            // PiiField 仅标注于可空 string 列（NickName/Email/LastLoginIp）；按 Mode 擦。
            object? newValue = pii.Mode == PiiErase.Null ? null : $"REDACTED-{idShort}";
            p.SetValue(user, newValue);
        }

        // 显式匿名化关键登录标识（非 [PiiField] 但属身份信息）。
        user.UserName = $"anon-{idShort}";
        user.Password = hasher.Hash(Guid.NewGuid().ToString("N"));   // 失活密码：新随机哈希，原值不可逆推
        user.Enable = false;
    }
}
