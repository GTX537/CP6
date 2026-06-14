namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务操作结果（校验/状态机用）。携带 i18n 错误码（E-FIN-xxx）+ 可选格式化参数，不携带具体语言文字
/// ——由控制器/中间件按 culture 解析为本地化消息（与 BizException 同思路）。
/// </summary>
public class FinResult
{
    /// <summary>是否成功。</summary>
    public bool Ok { get; }

    /// <summary>失败时的 i18n 错误码（如 "E-FIN-107"）；成功为 null。</summary>
    public string? Code { get; }

    /// <summary>错误消息模板的格式化参数。</summary>
    public object[] Args { get; }

    private FinResult(bool ok, string? code, object[] args)
    {
        Ok = ok;
        Code = code;
        Args = args;
    }

    /// <summary>成功。</summary>
    public static FinResult Pass() => new(true, null, Array.Empty<object>());

    /// <summary>失败（带错误码 + 参数）。</summary>
    public static FinResult Fail(string code, params object[] args) =>
        new(false, code, args ?? Array.Empty<object>());
}
