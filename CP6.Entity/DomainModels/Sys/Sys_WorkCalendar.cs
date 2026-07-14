using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CP6.Entity;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>工作日历例外表（WFS infra ①，spec §2.1）。周末默认非工作日，本表双向反转：
/// IsWorkday=true=补班（周末却上班）；false=假日（工作日却休）。unique(TenantId,Date)。</summary>
[Table("Sys_WorkCalendar")]
public class Sys_WorkCalendar : BaseTenantEntity
{
    /// <summary>例外日（date 粒度，存本地日期午夜）。</summary>
    public DateTime Date { get; set; }

    /// <summary>true=补班；false=假日。</summary>
    public bool IsWorkday { get; set; }

    /// <summary>"元日" / "振替休日" / "臨時休業" 等。</summary>
    [MaxLength(100)]
    public string? Note { get; set; }
}
