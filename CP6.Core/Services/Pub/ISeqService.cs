namespace CP6.Core.Services.Pub;

/// <summary>富采番服务 —— PUB 章05 §3。按业务键生成号码（前缀+日期段+周期重置流水）。</summary>
public interface ISeqService
{
    /// <summary>取业务键下一个号码（即时提交）。未配置该业务键抛 E-PUB-051。</summary>
    Task<string> NextAsync(string bizKey);
}
