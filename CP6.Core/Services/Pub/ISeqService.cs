namespace CP6.Core.Services.Pub;

/// <summary>富采番服务 —— PUB 章05 §3。按业务键生成号码（前缀+日期段+周期重置流水）。</summary>
public interface ISeqService
{
    /// <summary>
    /// 取业务键下一个号码。PostgreSQL 无调用方事务时即时提交；已有事务时加入该事务，
    /// 号码提交前为暂定值，调用方不得把它视为独立持久分配回执。未配置抛 E-PUB-051。
    /// </summary>
    Task<string> NextAsync(string bizKey);
}
