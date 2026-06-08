using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Msbb.Domain.Entities
{
    [Table("M_GENERAL")]
    public class GeneralMaster
    {
        // 复合主键1：分类代码 (如 UNIT_TYPE)
        [MaxLength(50)]
        [Column(Order = 1)]
        public string ClassCode { get; set; } = string.Empty;

        // 复合主键2：明细代码 (如 01)
        [MaxLength(50)]
        [Column(Order = 2)]
        public string Code { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? Value1 { get; set; } // 拓展文本值

        public decimal? NumValue1 { get; set; } // 拓展数值

        public int DisplayOrder { get; set; }
        // --- 修正：新增所有缺失的审计字段 ---
        public bool DelFlg { get; set; } = false;

        [MaxLength(50)]
        public string InsUsrID { get; set; } = "SYSTEM"; // 修正
        public DateTime InsDate { get; set; } = DateTime.Now;

        [MaxLength(50)]
        public string? UpdUsrID { get; set; }
        public DateTime? UpdDate { get; set; }
    }
}