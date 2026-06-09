using CP6.Entity.DTOs;

namespace CP6.Core.Services.Erp;

/// <summary>
/// MSBBPA110/120 Web 取引先マスタ業務サービス
/// </summary>
public interface IBusinessPartnerService
{
    // ───── PA110 CRUD ─────

    Task<BusinessPartnerDto?> GetByCdAsync(string bpCd, bool includeDeleted = false);
    Task<bool> ExistsAsync(string bpCd);
    Task<string> CreateAsync(BusinessPartnerDto dto, string? userName, bool preRegister = false);
    Task UpdateAsync(string bpCd, BusinessPartnerDto dto, string? userName);
    Task DeleteAsync(string bpCd, byte[]? rowVersion, string? userName);

    /// <summary>登録時バリデーション（21 条 — 仕様書 §8）</summary>
    void Validate(BusinessPartnerDto dto, bool isEdit, BusinessPartnerDto? before, IList<string> errors);

    /// <summary>FLG 変更不可チェック（仕様書 §4 注記 — MSG-018）</summary>
    BpFlgChangeCheckResult CheckFlgChange(BusinessPartnerDto before, BusinessPartnerDto after);

    // ───── PA120 一覧 ─────

    Task<(List<BpListItemDto> Rows, int Total)> SearchAsync(BpQueryDto query);
    Task<byte[]> ExportListCsvAsync(BpQueryDto query);
}
