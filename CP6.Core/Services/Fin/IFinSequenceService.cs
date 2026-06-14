namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务采番服务（章01 §4，仿 MesSequence）。凭证号格式：{key}-{yyyy-MM}-{NNNNN}，按月归零。
/// </summary>
public interface IFinSequenceService
{
    /// <summary>取下一个采番号（如 "GL-2026-06-00012"）。</summary>
    /// <param name="seqKey">采番键，如 "GL"</param>
    /// <param name="date">采番日（决定 yyyy-MM 作用域；null=今天）</param>
    Task<string> NextAsync(string seqKey, DateTime? date = null);
}
