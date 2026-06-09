using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// 見積計算書（MSBBPA010）業務服務契約
/// </summary>
public interface IEstimateCalcService
{
    /// <summary>分页列表（MSBBPA020 调用；自动过滤 IsDeleted=false）</summary>
    Task<(List<EstimateCalcListItem> Rows, int Total)> GetPageListAsync(EstimateCalcQuery query);

    /// <summary>按 NO 获取详情（含工程明细）。削除/参照 模式也可读到软删数据</summary>
    Task<EstimateCalcDto?> GetByNoAsync(string qtnCalcNo, bool includeDeleted = false);

    /// <summary>
    /// 新建（登録）。自动采番 XXXXXXXX-01，主番=当前最大+1
    /// 返回新创建记录的 QtnCalcNo
    /// </summary>
    Task<string> CreateAsync(EstimateCalcDto dto, string? userName);

    /// <summary>
    /// 修改（訂正）。根据 RowVersion 乐观锁；版本冲突抛 ConcurrencyException
    /// 工程明细执行 diff：新增行插入、已有行 Update、DTO 中缺失的行软删除
    /// </summary>
    Task UpdateAsync(string qtnCalcNo, EstimateCalcDto dto, string? userName);

    /// <summary>
    /// 逻辑删除（削除）。IsDeleted=1，不物理删除
    /// </summary>
    Task DeleteAsync(string qtnCalcNo, byte[]? rowVersion, string? userName);

    /// <summary>
    /// 复制（コピー）。读取来源记录，清空 NO+RowVersion，重新采番，写入新记录
    /// 自动设置 RefQtnCalcNo=来源 NO
    /// </summary>
    Task<string> CopyAsync(string sourceQtnCalcNo, string? userName);

    /// <summary>
    /// 計算。不写库，只根据 DTO 和主数据（M_GenericCode Paper/M067）计算：
    ///   EstimateSqm / StandardUnitPrice / EstimateUnitPrice / ConfirmedUnitPrice
    /// 返回 EstimateCalcResult，含金额明细行和过程说明。
    /// 如果 PaperCdF 能在 M_GenericCode(Paper) 找到，用 Num1 当単価；否则用 30 円/m² 作 fallback。
    /// </summary>
    Task<EstimateCalcResult> CalculateAsync(EstimateCalcDto dto);
}
