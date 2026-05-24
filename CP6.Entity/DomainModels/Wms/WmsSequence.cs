using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wms;

/// <summary>
/// WMS 業務NO 採番テーブル
/// </summary>
/// <remarks>
/// (Prefix + DateKey) ごとに NextNo を保持。MesSequence と同様の方式。
/// Prefix 例：IN / RC / OUT / SHIP / PKG / ST / QC / RMA / PLT / ROLL / TXN
/// </remarks>
[Table("T_WmsSequence")]
public class WmsSequence : BaseBizEntity
{
    /// <summary>採番プレフィックス</summary>
    [Required, MaxLength(10)]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>日付キー（YYYYMMDD）</summary>
    [Required, MaxLength(8)]
    public string DateKey { get; set; } = string.Empty;

    /// <summary>次番（採番後 +1）</summary>
    public int NextNo { get; set; } = 1;
}
