using CP6.Entity.DTOs.Mes;

namespace CP6.Core.Services.Mes;

/// <summary>設備管理サービス契約（マスタ + 停止記録）</summary>
public interface IMachineService
{
    // ───── 設備マスタ ─────
    Task<List<MachineDto>> SearchAsync(MachineSearchQuery query);
    Task<MachineDto?> GetByCdAsync(string machineCd);
    Task<string> CreateAsync(MachineDto dto, string? userName);
    Task UpdateAsync(string machineCd, MachineDto dto, string? userName);
    Task DeleteAsync(string machineCd, string? userName);

    /// <summary>設備状態変更（リアルタイム）</summary>
    Task ChangeStatusAsync(string machineCd, int status, string? userName);

    // ───── 停止記録 ─────
    Task<PagedResultDto<MachineDowntimeDto>> SearchDowntimesAsync(DowntimeSearchQuery query);
    Task<string> RegisterDowntimeAsync(MachineDowntimeDto dto, string? userName);
    Task CloseDowntimeAsync(string downtimeNo, DateTime endTime, string? recoveryOperatorCd, string? userName);
}
