// 定义于 Core 供服务层抛出；namespace 保留历史值 CP6.WebApi.Localization 以零涟漪迁移（2026-07-07 波4）。
namespace CP6.WebApi.Localization;

/// <summary>
/// 业务异常：只携带 i18n 错误码（如 "E10022"）+ 可选格式化参数，绝不携带具体语言文字。
/// 由 <see cref="CP6.WebApi.Middleware.BizExceptionMiddleware"/> 在请求 culture 下解析为本地化消息。
///
/// 用法：业务层 <c>throw new BizException("E10022")</c> 而非 <c>throw new Exception("必須項目…")</c>。
/// 这样后端文案随 culture 走、随 Sys_Lang 改、永不需要为改文案重新发版。
/// </summary>
public class BizException : Exception
{
    /// <summary>i18n 词条 key（错误码）。</summary>
    public string Code { get; }

    /// <summary>消息模板的格式化参数（对应 IStringLocalizer 的 string.Format 占位）。</summary>
    public object[] Args { get; }

    /// <summary>返回的 HTTP 状态码，默认 400。</summary>
    public int HttpStatus { get; }

    public BizException(string code, params object[] args) : base(code)
    {
        Code = code;
        Args = args ?? Array.Empty<object>();
        HttpStatus = 400;
    }

    public BizException(string code, int httpStatus, params object[] args) : base(code)
    {
        Code = code;
        Args = args ?? Array.Empty<object>();
        HttpStatus = httpStatus;
    }
}
