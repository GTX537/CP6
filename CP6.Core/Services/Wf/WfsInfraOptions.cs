namespace CP6.Core.Services.Wf;

/// <summary>WFS 引擎基建配置（波⑤，绑 <c>Wfs</c> 段，AddSingleton）。FireHour/保留期/滞留告警/默认时区。</summary>
public sealed class WfsInfraOptions
{
    public int WorkdayFireHour { get; set; } = 9;         // Wfs:WorkdayFireHour
    public int CleanupRetentionDays { get; set; } = 180;   // Wfs:CleanupRetentionDays（<=0 禁用清理）
    public int StaleReservationAlertDays { get; set; } = 7; // Wfs:StaleReservationAlertDays
    public string? DefaultTimeZone { get; set; }            // Wfs:DefaultTimeZone（null→服务器本地）
}
