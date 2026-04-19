using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 担当者マスタ（负责人主数据）
/// MSBBPA010: 受注拠点变更时联动过滤担当者
/// </summary>
[Table("M_Staff")]
public class MasterStaff : BaseBizEntity
{
    /// <summary>担当者 CD（业务键）</summary>
    [Required, MaxLength(10)]
    public string StaffCd { get; set; } = string.Empty;

    /// <summary>担当者姓名</summary>
    [Required, MaxLength(100)]
    public string StaffName { get; set; } = string.Empty;

    /// <summary>所属据点 CD（联动过滤用）</summary>
    [Required, MaxLength(10)]
    public string BaseCd { get; set; } = string.Empty;

    /// <summary>关联的 Sys_User Id（可选，便于登录用户关联默认据点/担当者）</summary>
    public Guid? SysUserId { get; set; }

    public int SortOrder { get; set; }
}
