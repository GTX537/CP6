using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace CP6.WebApi.Controllers;

/// <summary>
/// i18n 优化：控制器基类，惰性暴露「请求级」<see cref="IStringLocalizer"/>（读 Sys_Lang，按当前
/// 请求 culture 解析，复用 CacheService）。用于把 <c>return XxxResult(new { message = 原文 })</c>
/// 里的硬编码文案本地化。
/// <para>用法：message 处用索引器包裹原文作 key（带参数用位置占位 {0}/{1}，IStringLocalizer 走
/// string.Format，仅支持位置参数不支持命名）。</para>
/// <para>HttpContext 不可用时（如单元测试直接 new 控制器）回退到 <see cref="EchoStringLocalizer"/>，
/// 原样回显 key，既不抛异常也保持原文行为。</para>
/// </summary>
public abstract class LocalizedControllerBase : ControllerBase
{
    private IStringLocalizer? _localizer;

    /// <summary>请求级本地化器（单例 DbStringLocalizer，按 CurrentUICulture 解析）；无 HttpContext 时回显 key。</summary>
    protected IStringLocalizer Localizer =>
        _localizer ??= HttpContext?.RequestServices?.GetService<IStringLocalizer>() ?? EchoStringLocalizer.Instance;
}

/// <summary>
/// 兜底本地化器：原样回显 key（带位置参数则 string.Format）。用于 HttpContext 不可用的场景
/// （单元测试直接实例化控制器），避免 NullReference 并保持「未本地化＝原文」的行为。
/// </summary>
internal sealed class EchoStringLocalizer : IStringLocalizer
{
    public static readonly EchoStringLocalizer Instance = new();

    public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, arguments.Length > 0 ? string.Format(name, arguments) : name, resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
        System.Array.Empty<LocalizedString>();
}
