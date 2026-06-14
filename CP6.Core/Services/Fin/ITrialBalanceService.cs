namespace CP6.Core.Services.Fin;

/// <summary>试算平衡表服务（章02 §2）。三栏 + 两层平衡校验（数据完整性探针）。</summary>
public interface ITrialBalanceService
{
    /// <summary>按期间构建三栏试算表（期初含历史；实时滚算 F-D5）。期间不存在抛 E-FIN-140。</summary>
    Task<TrialBalance> BuildAsync(Guid periodId);
}
